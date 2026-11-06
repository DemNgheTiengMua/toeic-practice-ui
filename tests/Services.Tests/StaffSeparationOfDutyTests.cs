using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #201 / ADM-02 / ADM-03 / ADM-04: the database refuses a self-approval and a triage write, and the
/// application declares the acting account so those refusals have something to read.
///
/// <para>
/// The schema test <c>docs/schema-tests/domain35-triage-and-selfapproval.sql</c> drives every refusal
/// through a direct table write and is proved load-bearing by
/// <c>prove-guard-triage-selfapproval.sh</c>. What it cannot prove is that the deployed application
/// ever DECLARES an actor: a trigger that reads <c>LastWrittenBy</c> refuses nothing if no code
/// assigns it. That is what this file covers, through the real <see cref="StaffAccountStampInterceptor"/>
/// and the real database.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class StaffSeparationOfDutyTests(DatabaseFixture database)
{
    private sealed class Acting(int? userId) : IActingStaffAccount
    {
        public int? UserId { get; } = userId;
    }

    private CompanionDbContext CreateDb(int? actingUserId)
    {
        var options = new DbContextOptionsBuilder<CompanionDbContext>()
            .UseSqlServer(database.ConnectionString)
            .AddInterceptors(new StaffAccountStampInterceptor(new Acting(actingUserId)))
            .Options;
        return new CompanionDbContext(options);
    }

    private async Task RunSqlAsync(string sql)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> ScalarAsync(string sql)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    /// <summary>
    /// Seeds one author, one triage-only account, a certificate, a skill, a topic and a certificate
    /// with everything a version and a section need. Returns the ids the tests use.
    /// </summary>
    private async Task<(int Author, int Triage, int ItemId, int TopicId, int StimulusId, int SkillId, int CertId)>
        SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        await RunSqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = 'operations.support.triage')
              INSERT INTO Permissions (Code) VALUES
                ('pool.content.author'), ('pool.content.review'), ('assessment.blueprint.design'),
                ('operations.support.triage'), ('system.governance.admin');

            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'sd-author-{suffix}')
              INSERT INTO Roles (Code, Name) VALUES ('sd-author-{suffix}', N'Author');
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'sd-triage-{suffix}')
              INSERT INTO Roles (Code, Name) VALUES ('sd-triage-{suffix}', N'Triage');

            DECLARE @ra INT = (SELECT RoleId FROM Roles WHERE Code = 'sd-author-{suffix}');
            DECLARE @rt INT = (SELECT RoleId FROM Roles WHERE Code = 'sd-triage-{suffix}');
            INSERT INTO RolePermissions (RoleId, PermissionCode) VALUES (@ra, 'pool.content.author');
            INSERT INTO RolePermissions (RoleId, PermissionCode) VALUES (@rt, 'operations.support.triage');
            """);

        await RunSqlAsync($"""
            INSERT INTO Users (RoleId, Email, PasswordHash)
              SELECT RoleId, 'sd-author-{suffix}@test.local', 0x01 FROM Roles WHERE Code = 'sd-author-{suffix}';
            INSERT INTO Users (RoleId, Email, PasswordHash)
              SELECT RoleId, 'sd-triage-{suffix}@test.local', 0x02 FROM Roles WHERE Code = 'sd-triage-{suffix}';
            """);

        await RunSqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Certificates WHERE Code = 'SD{suffix}')
              INSERT INTO Certificates (Code, Name) VALUES ('SD{suffix}', N'SD certificate');
            """);

        var certId = await ScalarAsync($"SELECT CertId FROM Certificates WHERE Code = 'SD{suffix}'");
        var authorId = await ScalarAsync($"SELECT UserId FROM Users WHERE Email = 'sd-author-{suffix}@test.local'");
        var triageId = await ScalarAsync($"SELECT UserId FROM Users WHERE Email = 'sd-triage-{suffix}@test.local'");

        await RunSqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Skills WHERE CertId = {certId} AND Code = 'SDR')
              INSERT INTO Skills (CertId, Code, Name, Modality) VALUES ({certId}, 'SDR', N'SD reading', 'reading');
            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-GIST')
              INSERT INTO Topics (Modality, Code, Name) VALUES ('reading', 'R-GIST', N'Global gist');
            """);

        var skillId = await ScalarAsync($"SELECT SkillId FROM Skills WHERE CertId = {certId} AND Code = 'SDR'");
        var topicId = await ScalarAsync("SELECT TopicId FROM Topics WHERE Modality = 'reading' AND Code = 'R-GIST'");

        await RunSqlAsync($"INSERT INTO Stimuli (CefrLevel, Passage, DomainContext) VALUES ('B1', N'SD passage.', 'general');");
        var stimulusId = await ScalarAsync("SELECT MAX(StimulusId) FROM Stimuli");

        await RunSqlAsync($"""
            INSERT INTO Items (StimulusId, Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus)
              VALUES ({stimulusId}, 'reading', {topicId}, 'B1', 'passage_comprehension', 'demo', 'validated');
            """);
        var itemId = await ScalarAsync("SELECT MAX(ItemId) FROM Items");

        return (authorId, triageId, itemId, topicId, stimulusId, skillId, certId);
    }

    /// <summary>
    /// The seam that keeps the triggers alive. Without it every write declares nothing, and both
    /// inverses are unenforceable in production no matter how good the trigger is.
    /// </summary>
    [Fact]
    public async Task The_interceptor_stamps_the_acting_account_onto_an_item_version()
    {
        var seed = await SeedAsync();

        await using (var db = CreateDb(seed.Author))
        {
            db.ItemVersions.Add(new ItemVersion
            {
                ItemId = seed.ItemId,
                VersionNumber = 1,
                Stem = "A stem?",
                OptionA = "a", OptionB = "b", OptionC = "c", OptionD = "d",
                KeyLabel = "A",
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(seed.Author, await ScalarAsync(
            $"SELECT LastWrittenBy FROM ItemVersions WHERE ItemId = {seed.ItemId} AND VersionNumber = 1"));
    }

    /// <summary>
    /// The interceptor OVERWRITES a name the caller supplied. This is what closes impersonation on the
    /// mediated path: the declaration comes from the JWT, so a service cannot vouch for somebody else,
    /// and a triage account cannot declare an author to slip past the trigger.
    ///
    /// <para>
    /// Without this property the column would be a suggestion, and a caller that set it to an author's
    /// id would be accepted - the "declare somebody else" hole the schema comment states a trigger
    /// cannot close on its own.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_interceptor_overwrites_a_name_the_caller_supplied()
    {
        var seed = await SeedAsync();

        await using (var db = CreateDb(seed.Triage))
        {
            db.ItemVersions.Add(new ItemVersion
            {
                ItemId = seed.ItemId,
                VersionNumber = 2,
                Stem = "A stem?",
                OptionA = "a", OptionB = "b", OptionC = "c", OptionD = "d",
                KeyLabel = "A",
                // The caller claims to be the author. Triage is who is actually writing.
                LastWrittenBy = seed.Author,
            });

            // The trigger sees the OVERWRITTEN value - triage - and refuses. Had the interceptor
            // trusted the caller, this row would have been accepted as the author's work.
            var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("ADM-03", refused.InnerException?.Message ?? refused.Message);
        }

        Assert.Equal(0, await ScalarAsync(
            $"SELECT COUNT(*) FROM ItemVersions WHERE ItemId = {seed.ItemId} AND VersionNumber = 2"));
    }

    /// <summary>
    /// ADM-03 through the application: a triage-only account saving a section is refused by the
    /// database, not by a controller. The service layer has no check for this at all, which is the
    /// point - the same write succeeds for an author immediately after.
    /// </summary>
    [Fact]
    public async Task Triage_cannot_save_a_section_but_an_author_can()
    {
        var seed = await SeedAsync();

        await using (var db = CreateDb(seed.Triage))
        {
            db.Sections.Add(new Section
            {
                SkillId = seed.SkillId,
                CertId = seed.CertId,
                Modality = "reading",
                Code = "SD-TRIAGE",
                Name = "Triage section",
                OptionCount = 4,
                ExpectedQuestionCount = 10,
                DisplayOrder = 90,
            });

            var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("ADM-03", refused.InnerException?.Message ?? refused.Message);
        }

        // The identical write by an author is accepted, so the refusal above was about the account.
        await using (var db = CreateDb(seed.Author))
        {
            db.Sections.Add(new Section
            {
                SkillId = seed.SkillId,
                CertId = seed.CertId,
                Modality = "reading",
                Code = "SD-TRIAGE",
                Name = "Triage section",
                OptionCount = 4,
                ExpectedQuestionCount = 10,
                DisplayOrder = 90,
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, await ScalarAsync(
            $"SELECT COUNT(*) FROM Sections WHERE SkillId = {seed.SkillId} AND Code = 'SD-TRIAGE'"));
    }

    /// <summary>
    /// ADM-02 through the application: the uploader saving a review decision for its own batch is
    /// refused by the trigger. The account holds pool.content.author, so this is not a permission
    /// check - no combination of namespaces can make it pass.
    /// </summary>
    [Fact]
    public async Task The_uploader_cannot_claim_a_review_sample_for_its_own_batch()
    {
        var seed = await SeedAsync();

        await RunSqlAsync($"""
            INSERT INTO ImportBatches (CertId, SourceName, Status, UploadedBy)
              VALUES ({seed.CertId}, N'sd-batch', 'done', {seed.Author});
            """);
        var batchId = await ScalarAsync($"SELECT MAX(BatchId) FROM ImportBatches WHERE SourceName = N'sd-batch'");

        await using (var db = CreateDb(seed.Author))
        {
            db.ReviewSampleDecisions.Add(new ReviewSampleDecision
            {
                BatchId = batchId,
                ItemId = seed.ItemId,
                ActorUserId = seed.Author,
                Decision = "claim",
            });

            var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("ADM-02", refused.InnerException?.Message ?? refused.Message);
        }

        // A different account on the same batch is accepted. A trigger that refused every decision
        // would pass the assertion above and prove nothing.
        await using (var db = CreateDb(seed.Triage))
        {
            db.ReviewSampleDecisions.Add(new ReviewSampleDecision
            {
                BatchId = batchId,
                ItemId = seed.ItemId,
                ActorUserId = seed.Triage,
                Decision = "claim",
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, await ScalarAsync($"SELECT COUNT(*) FROM ReviewSampleDecisions WHERE BatchId = {batchId}"));
    }
}
