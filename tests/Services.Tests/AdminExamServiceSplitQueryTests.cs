using System.Text;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// F5: a read that loads more than one collection navigation in one query is a Cartesian product, which
/// EF warns about (20504) and which multiplies rows by every child added. The guard below turns that
/// warning into a failure, so reintroducing one breaks a test instead of quietly costing latency.
/// <para>
/// #269: the graph these reads walk is now the pool's - a section's groups are the stimuli its positions
/// bind, and each group's questions are positions with their version's options projected from four
/// columns. The legacy <c>QuestionGroups → Questions → QuestionOptions</c> shape is gone, but the hazard
/// is not: a future <c>Include</c> of two collections on this projection is the same mistake, so the
/// strict context and the reads it guards both survive. Only the fixture changed.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AdminExamServiceSplitQueryTests(DatabaseFixture database)
{
    /// <summary>The app's DbContext stack, except a multi-collection include is fatal.</summary>
    private CompanionDbContext CreateStrictDb()
    {
        var options = new DbContextOptionsBuilder<CompanionDbContext>()
            .UseSqlServer(database.ConnectionString)
            .ConfigureWarnings(w => w.Throw(RelationalEventId.MultipleCollectionIncludeWarning))
            .Options;
        return new CompanionDbContext(options);
    }

    [Fact]
    public async Task The_readiness_audit_splits_its_collection_includes()
    {
        var examId = await SeedExamWithTwoGroupsAsync();

        await using var db = CreateStrictDb();
        var audit = new AdminExamService(db, TimeProvider.System);

        // Throws InvalidOperationException citing 20504 if a split-query call is ever removed.
        var result = await audit.AuditExamPublishReadinessAsync(examId);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task The_exam_studio_splits_its_collection_includes_and_keeps_every_question_and_option()
    {
        var examId = await SeedExamWithTwoGroupsAsync();

        await using var db = CreateStrictDb();
        var studio = await new AdminExamService(db, TimeProvider.System).GetExamStudioAsync(examId);

        var groups = studio.Sections.SelectMany(s => s.Groups).ToList();
        Assert.Equal(2, groups.Count);
        // CNT-06 fixes a version at four options, so the count a reader sees is four - what must survive
        // the split is that every option of the one question in each group is still there.
        Assert.All(groups, g => Assert.Equal(new[] { "A", "B", "C", "D" }, Assert.Single(g.Questions).Options.Select(o => o.Label)));
    }

    [Fact]
    public async Task The_question_list_splits_its_collection_includes_and_keeps_every_question_and_option()
    {
        var examId = await SeedExamWithTwoGroupsAsync();

        await using var db = CreateStrictDb();
        var list = await new AdminExamService(db, TimeProvider.System).GetExamQuestionsAsync(examId);

        Assert.Equal(2, list.Groups.Count);
        Assert.All(list.Groups, g => Assert.Equal(new[] { "A", "B", "C", "D" }, Assert.Single(g.Questions).Options.Select(o => o.Label)));
    }

    [Fact]
    public async Task Both_exam_exports_split_their_collection_includes()
    {
        var examId = await SeedExamWithTwoGroupsAsync();

        await using var db = CreateStrictDb();
        var fromStudio = await new AdminExamService(db, TimeProvider.System).ExportExamPackageAsync(examId, "json");
        var fromImports = await new QuestionImportService(db, new RecordingFileStore(), TimeProvider.System).ExportExamPackageAsync(examId, "json");

        foreach (var (stream, _, _) in new[] { fromStudio, fromImports })
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var manifest = await reader.ReadToEndAsync();
            Assert.Contains("first", manifest);
            Assert.Contains("second", manifest);
        }
    }

    /// <summary>
    /// Two groups, each holding one position with four options: the shape that used to explode into
    /// groups x questions x options rows. The groups are two stimuli, which is what a pool "group" is.
    /// </summary>
    private async Task<int> SeedExamWithTwoGroupsAsync()
    {
        var certCode = $"SPLIT_{Guid.NewGuid():N}"[..18];

        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            DECLARE @certStr VARCHAR(10) = CAST(@cert AS VARCHAR(10));

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'SL_S_' + @certStr, N'Listening', 'listening', 1);

            DECLARE @sL INT = (SELECT SkillId FROM Skills WHERE CertId = @cert);

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder) VALUES
              (@sL, @cert, 'listening', 'SL_' + @certStr, N'Listening Part 1', 1, 1);
            """);

        await using var seed = DbContextFactory.Create(database.ConnectionString);
        var service = new AdminExamService(seed, TimeProvider.System);
        var cert = await seed.Certificates.SingleAsync(c => c.Code == certCode);
        var sectionId = (await seed.Sections.SingleAsync(s => s.CertId == cert.CertId)).SectionId;

        var exam = await service.CreateExamAsync(new CreateAdminExamRequest(
            cert.CertId, $"Split {Guid.NewGuid():N}"[..25], 40, ExamPurposeDto.Mock));

        await PoolFixture.AddMcqAsync(seed, exam.ExamId, cert.CertId, sectionId, "listening", "first", displayOrder: 1,
            passage: "The first passage.");
        await PoolFixture.AddMcqAsync(seed, exam.ExamId, cert.CertId, sectionId, "listening", "second", displayOrder: 2,
            passage: "The second passage.");

        return exam.ExamId;
    }
}
