using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #190: the item lifecycle, from the service side.
/// <para>
/// The schema half — the six states, the ten named transitions, and both halves of the no-delete rule —
/// is proved in T-SQL by <c>docs/schema-tests/domain26-item-lifecycle.sql</c>. What only a service can
/// show is here: that selection reads <em>active items of one tier</em> and review reads
/// <em>neither lifecycle nor tier</em>.
/// </para>
/// <para>
/// Every fixture authors its own items under a unique batch tag, because the pool's cells are shared
/// with every other fixture in the suite and a neighbouring test's active item would otherwise be
/// drawn. The topic vocabulary is a closed set of twenty, so the topic is resolved by canonical code
/// rather than invented.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemLifecycleTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private PracticeService CreatePractice() =>
        new(DbContextFactory.Create(database.ConnectionString),
            new LearnerService(DbContextFactory.Create(database.ConnectionString)));

    private AttemptEngine CreateEngine() => new(DbContextFactory.Create(database.ConnectionString));

    /// <summary>One reading topic, one certificate, and one learner on it. No exam: a pool drill has none.</summary>
    private async Task<(int CertId, int TopicId, int UserId)> SeedCellAsync(string certCode)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');

            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Lifecycle Cert', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes, MinScaledScore, MaxScaledScore, ScoreStep)
            VALUES (@cert, 'RDT', N'Reading', 'reading', 1, 60, 100, 990, 10);

            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-COH')
              INSERT INTO Topics (Modality, Code, Name)
              VALUES ('reading', 'R-COH', N'Discourse cohesion, reference, text insertion');

            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Lifecycle Learner', @cert);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return (
            await db.Certificates.Where(c => c.Code == certCode).Select(c => c.CertId).SingleAsync(),
            await db.Topics.Where(t => t.Modality == "reading" && t.Code == "R-COH").Select(t => t.TopicId).SingleAsync(),
            await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync());
    }

    /// <summary>One item with one version. The caller supplies the two columns under test.</summary>
    private async Task<int> SeedItemAsync(
        int topicId, string level, string tier, string lifecycle, string tag, string stem)
    {
        await database.SqlAsync($"""
            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
            VALUES ('reading', {topicId}, '{level}', 'sentence_cloze', '{tier}', '{lifecycle}', '{tag}');
            DECLARE @item INT = SCOPE_IDENTITY();
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation)
            VALUES (@item, 1, N'{stem}', N'{stem}-a', N'{stem}-b', N'{stem}-c', N'{stem}-d', 'A', N'Why {stem}.');
            SELECT @item AS ItemId;
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Items.Where(i => i.BatchTag == tag).Select(i => i.ItemId).SingleAsync();
    }

    /// <summary>
    /// AC2: selection returns active items of the requested tier and nothing else.
    /// <para>
    /// The cell holds four items that differ in exactly one column each — active production, active
    /// demo, retired production, quarantined production — so a selector that drops any one of the two
    /// filters draws a fifth answer, and the assertion names which one it drew.
    /// </para>
    /// <para>
    /// The trap this test is written against: a selector that filters on lifecycle only, or on tier
    /// only, passes a fixture that holds a single active production item. That is why all three
    /// wrong-shape items sit in the same cell at the same level as the right one.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Selection_draws_active_production_items_and_nothing_else()
    {
        var certCode = Unique("LCY");
        var (_, topicId, userId) = await SeedCellAsync(certCode);
        var tag = Unique("tier");

        var wantedId = await SeedItemAsync(topicId, "B2", "production", "active", $"{tag}-wanted", "The one item that should be drawn.");
        await SeedItemAsync(topicId, "B2", "demo", "active", $"{tag}-demo", "Demo-tier stock, active but the wrong tier.");
        await SeedItemAsync(topicId, "B2", "production", "retired", $"{tag}-retired", "Production, withdrawn from selection.");
        await SeedItemAsync(topicId, "B2", "production", "quarantined", $"{tag}-quarantined", "Production, quarantined by telemetry.");
        await SeedItemAsync(topicId, "B2", "production", "validated", $"{tag}-validated", "Production, audited but not live.");
        await SeedItemAsync(topicId, "B2", "production", "retired_defective", $"{tag}-defective", "Production, confirmed defective.");

        // The cell also holds whatever another fixture left active in it. Everything this test did not
        // author is removed from the answer before asserting, so the claim is about the six shapes
        // above and not about a neighbour's stock. R-COH is this class's own topic — no other fixture
        // in the suite authors a reading item on it — so "neighbour" here means one of this class's
        // own other tests, and retiring it cannot silently protect a real defect.
        await database.SqlAsync($"""
            UPDATE Items SET LifecycleStatus = 'retired'
            WHERE TopicId = {topicId} AND CefrLevel = 'B2' AND LifecycleStatus = 'active'
              AND (BatchTag IS NULL OR BatchTag NOT LIKE '{tag}%');
            """);

        var practice = CreatePractice();
        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(topicId, CefrLevel.B2));

        var drawn = await practice.GetSessionQuestionsAsync(userId, session.SessionId);

        var drawnVersionIds = drawn.Select(q => q.ItemVersionId).ToList();
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var drawnItems = await db.ItemVersions
            .Where(v => drawnVersionIds.Contains(v.ItemVersionId))
            .Select(v => new { v.ItemVersionId, v.ItemId, v.Item.LifecycleStatus, v.Item.ContentTier, v.Item.BatchTag })
            .ToListAsync();

        Assert.All(drawnItems, item =>
        {
            Assert.Equal("active", item.LifecycleStatus);
            Assert.Equal("production", item.ContentTier);
        });
        Assert.Equal(new[] { wantedId }, drawnItems.Select(i => i.ItemId).ToArray());

        // the two columns are named in the failure too, so a selector that dropped one of them says
        // which of the five wrong shapes it let through
        Assert.DoesNotContain(drawnItems, i => i.BatchTag == $"{tag}-demo");
        Assert.DoesNotContain(drawnItems, i => i.BatchTag == $"{tag}-retired");
        Assert.DoesNotContain(drawnItems, i => i.BatchTag == $"{tag}-quarantined");
        Assert.DoesNotContain(drawnItems, i => i.BatchTag == $"{tag}-validated");
        Assert.DoesNotContain(drawnItems, i => i.BatchTag == $"{tag}-defective");
    }

    /// <summary>
    /// AC2, the other half: the same cell at one tier and a different level is a different cell. A
    /// selector that ignored the level would draw the B1 item into a B2 drill, which is the defect the
    /// cell index exists to prevent — and it would look green against a single-level fixture.
    /// </summary>
    [Fact]
    public async Task Selection_stays_inside_the_requested_level()
    {
        var certCode = Unique("LCY");
        var (_, topicId, userId) = await SeedCellAsync(certCode);
        var tag = Unique("lvl");

        var b2 = await SeedItemAsync(topicId, "B2", "production", "active", $"{tag}-b2", "The B2 item.");
        var b1 = await SeedItemAsync(topicId, "B1", "production", "active", $"{tag}-b1", "The B1 item, a different cell.");

        await database.SqlAsync($"""
            UPDATE Items SET LifecycleStatus = 'retired'
            WHERE TopicId = {topicId} AND CefrLevel = 'B2' AND LifecycleStatus = 'active'
              AND (BatchTag IS NULL OR BatchTag NOT LIKE '{tag}%');
            """);

        var practice = CreatePractice();
        var session = await practice.StartSessionAsync(userId, new StartPracticeSessionInput(topicId, CefrLevel.B2));
        var drawn = await practice.GetSessionQuestionsAsync(userId, session.SessionId);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var drawnItemIds = await db.ItemVersions
            .Where(v => drawn.Select(q => q.ItemVersionId).Contains(v.ItemVersionId))
            .Select(v => v.ItemId)
            .ToListAsync();

        Assert.Equal(new[] { b2 }, drawnItemIds.ToArray());
        Assert.DoesNotContain(b1, drawnItemIds);
    }

    /// <summary>
    /// AC4: a retired item's past attempt still renders, and the review filters on neither lifecycle
    /// nor tier.
    /// <para>
    /// The assertion is strong on purpose. The item is moved to <c>retired_defective</c> — the worst
    /// state — and its tier is flipped to <c>demo</c> while the paper that bound it is
    /// <c>production</c>. A review that filtered on either lifecycle or tier would render nothing, or
    /// an error, where the learner must see the question they sat.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_past_attempt_renders_after_its_item_is_retired_defective_and_demoted()
    {
        var certCode = Unique("LCY");
        var (certId, topicId, userId) = await SeedCellAsync(certCode);
        var tag = Unique("rev");

        // a paper around a pool item, the #185/#186 shape
        await database.SqlAsync($"""
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
            VALUES ((SELECT SkillId FROM Skills WHERE CertId = {certId}), {certId}, 'reading', 'P5', N'Incomplete Sentences', 4, 1, 1);
            DECLARE @sec INT = SCOPE_IDENTITY();

            -- a finished attempt is scored, so the certificate needs a scale and a band to land in
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              ({certId}, 'LOW', N'Low', 0, 499, 1), ({certId}, 'HIGH', N'High', 500, 990, 2);
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
              ({certId}, (SELECT SkillId FROM Skills WHERE CertId = {certId}), NULL, 0, 100),
              ({certId}, (SELECT SkillId FROM Skills WHERE CertId = {certId}), NULL, 1, 500);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount, ContentTier)
            VALUES ({certId}, N'Retired-item paper', 'published', 60, 'mock', 1, 'production');
            DECLARE @exam INT = SCOPE_IDENTITY();

            -- #272: the position is a pool binding only. The legacy question and its options that used to
            -- sit beside it are gone, so the sitting takes the roster branch - which is the shape
            -- production produces and the one this test is about.
            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
            VALUES ('reading', {topicId}, 'B1', 'sentence_cloze', 'production', 'active', '{tag}-item');
            DECLARE @item INT = SCOPE_IDENTITY();
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation)
            VALUES (@item, 1, N'The stem the learner actually sat.',
                    N'sat-a', N'sat-b', N'sat-c', N'sat-d', 'C', N'The explanation the learner was shown.');
            DECLARE @ver INT = SCOPE_IDENTITY();

            INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder)
            VALUES (@exam, @sec, {certId}, @ver, @item, 'reading', 1);

            SELECT @exam AS ExamId, @item AS ItemId, @ver AS ItemVersionId, @ver AS Ver;
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var examId = await db.Exams.Where(e => e.CertId == certId).Select(e => e.ExamId).SingleAsync();

        var engine = CreateEngine();
        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);
        // #272: the position is its ExamItems row, so that is what the sitting is addressed by.
        var questionId = await db.ExamItems.Where(b => b.ExamId == examId).Select(b => b.ExamItemId).SingleAsync();
        var versionId = await db.ExamItems.Where(b => b.ExamId == examId).Select(b => b.ItemVersionId).SingleAsync();

        // answer C: the pinned version's key, so the review has something correct to show
        await engine.SaveAnswerAsync(attempt.AttemptId, userId, questionId, versionId * 10 + 3, isFlagged: false);
        await engine.SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: false);

        // now the item is disgraced in both columns the review is accused of filtering on
        await database.SqlAsync($"""
            UPDATE Items SET LifecycleStatus = 'quarantined' WHERE BatchTag = '{tag}-item';
            UPDATE Items SET LifecycleStatus = 'retired_defective' WHERE BatchTag = '{tag}-item';
            UPDATE Items SET ContentTier = 'demo' WHERE BatchTag = '{tag}-item';
            """);

        var review = await CreateEngine().GetAttemptReviewAsync(attempt.AttemptId, userId);
        var question = review.Questions.Single(q => q.QuestionId == questionId);

        Assert.Equal("The stem the learner actually sat.", question.Stem);
        Assert.Equal("The explanation the learner was shown.", question.Explanation);
        Assert.Equal(
            new[] { "sat-a", "sat-b", "sat-c", "sat-d" },
            question.Options.Select(o => o.Text));
        Assert.True(question.IsCorrect);
        Assert.Equal(new[] { "C" }, question.Options.Where(o => o.IsCorrect).Select(o => o.Label));
    }

    /// <summary>
    /// The lifecycle guard is the database's, and it fires through a real save — not only through
    /// T-SQL. Without <c>HasTrigger</c> on <c>Items</c>, EF's OUTPUT clause fails on every save to the
    /// table and the guard looks like an EF bug.
    /// </summary>
    [Fact]
    public async Task A_transition_the_specification_does_not_name_is_refused_through_a_real_save()
    {
        var certCode = Unique("LCY");
        var (_, topicId, _) = await SeedCellAsync(certCode);
        var tag = Unique("tr");

        var itemId = await SeedItemAsync(topicId, "B2", "production", "active", $"{tag}-item", "An active item.");

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var item = await db.Items.SingleAsync(i => i.ItemId == itemId);
        item.LifecycleStatus = "draft";   // active -> draft is not a named transition

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("CNT-42", error.InnerException?.Message ?? error.Message);

        await db.Entry(item).ReloadAsync();
        Assert.Equal("active", item.LifecycleStatus);
    }

    /// <summary>
    /// The save to <c>Items</c> that the guard must <em>allow</em> still works, which is what proves
    /// the <c>HasTrigger</c> declarations are present: a missing one fails on the OUTPUT clause and
    /// looks exactly like a guard firing.
    /// </summary>
    [Fact]
    public async Task A_named_transition_saves_through_ef()
    {
        var certCode = Unique("LCY");
        var (_, topicId, _) = await SeedCellAsync(certCode);
        var tag = Unique("ok");

        var itemId = await SeedItemAsync(topicId, "B2", "production", "draft", $"{tag}-item", "A draft item.");

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var item = await db.Items.SingleAsync(i => i.ItemId == itemId);
        item.LifecycleStatus = "validated";
        await db.SaveChangesAsync();

        await db.Entry(item).ReloadAsync();
        Assert.Equal("validated", item.LifecycleStatus);
    }

    /// <summary>
    /// AC3, through a real save: a draft item may be deleted, one that has left draft may not. The
    /// first half is the one a blanket refusal fails, and it is on the same fixture as the second so
    /// neither can pass alone.
    /// </summary>
    [Fact]
    public async Task A_draft_item_is_deletable_and_a_retired_one_is_not()
    {
        var certCode = Unique("LCY");
        var (_, topicId, _) = await SeedCellAsync(certCode);
        var tag = Unique("del");

        var draftId = await SeedItemAsync(topicId, "B2", "production", "draft", $"{tag}-draft", "A draft nobody answered.");
        var retiredId = await SeedItemAsync(topicId, "B2", "production", "retired", $"{tag}-retired", "A retired item.");

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var draft = await db.Items.SingleAsync(i => i.ItemId == draftId);
            db.Items.Remove(draft);
            await db.SaveChangesAsync();
        }

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            Assert.Equal(0, await db.Items.CountAsync(i => i.ItemId == draftId));
        }

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var retired = await db.Items.SingleAsync(i => i.ItemId == retiredId);
            db.Items.Remove(retired);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Contains("CNT-43", error.InnerException?.Message ?? error.Message);
        }

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            Assert.Equal(1, await db.Items.CountAsync(i => i.ItemId == retiredId));
        }
    }
}
