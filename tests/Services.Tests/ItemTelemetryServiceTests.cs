using System;
using System.Collections.Generic;
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
/// #199 / D0.7 / CNT-44 / CNT-45: the service half of telemetry quarantine.
/// <para>
/// The arithmetic is proved in <c>ItemTelemetryRuleTests</c>; what only a service can show is here:
/// that a breaching item becomes <c>quarantined</c>, that it leaves selection immediately, that the
/// schedule is idempotent across its four checkpoints, and — the one rule that cannot be got wrong —
/// that a recalculation after a confirmed defect never downgrades a learner.
/// </para>
/// <para>
/// Every fixture authors its own items under a unique batch tag, and retires whatever else is active
/// in the same cell, because the pool's cells are shared with the whole suite.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ItemTelemetryServiceTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..18];

    private ItemTelemetryService CreateService() =>
        new(DbContextFactory.Create(database.ConnectionString), TimeProvider.System);

    private AttemptEngine CreateEngine() => new(DbContextFactory.Create(database.ConnectionString));

    private PracticeService CreatePractice() =>
        new(DbContextFactory.Create(database.ConnectionString),
            new LearnerService(DbContextFactory.Create(database.ConnectionString)));

    /// <summary>
    /// One reading topic, one certificate, one paper of <paramref name="questionCount"/> positions, and
    /// a two-band scale. Raw 0..2 map to 0/25/50 so a one-question difference crosses a band: 0 = LOW,
    /// 50 = HIGH.
    /// </summary>
    private async Task<(int CertId, int TopicId, int SkillId, int ExamId, int SectionId)> SeedCellAsync(string certCode)
    {
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');

            INSERT INTO Certificates (Code, Name, IsActive, ScoreAggregationRule)
            VALUES ('{certCode}', N'Telemetry Cert', 1, 'sum');
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes, MinScaledScore, MaxScaledScore, ScoreStep)
            VALUES (@cert, 'READ', N'Reading', 'reading', 1, 60, 0, 50, 1);
            DECLARE @skill INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder)
            VALUES (@skill, @cert, 'reading', 'P5', N'Incomplete Sentences', 4, 2, 1);
            DECLARE @sec INT = SCOPE_IDENTITY();

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'LOW', N'Low', 0, 49, 1), (@cert, 'HIGH', N'High', 50, 50, 2);
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
              (@cert, @skill, NULL, 0, 0), (@cert, @skill, NULL, 1, 25), (@cert, @skill, NULL, 2, 50);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount, ContentTier)
            VALUES (@cert, N'Telemetry paper', 'published', 60, 'mock', 2, 'production');
            DECLARE @exam INT = SCOPE_IDENTITY();

            IF NOT EXISTS (SELECT 1 FROM Topics WHERE Modality = 'reading' AND Code = 'R-COH')
              INSERT INTO Topics (Modality, Code, Name)
              VALUES ('reading', 'R-COH', N'Discourse cohesion, reference, text insertion');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var certId = await db.Certificates.Where(c => c.Code == certCode).Select(c => c.CertId).SingleAsync();
        return (
            certId,
            await db.Topics.Where(t => t.Modality == "reading" && t.Code == "R-COH").Select(t => t.TopicId).SingleAsync(),
            await db.Skills.Where(s => s.CertId == certId).Select(s => s.SkillId).SingleAsync(),
            await db.Exams.Where(e => e.CertId == certId).Select(e => e.ExamId).SingleAsync(),
            await db.Sections.Where(s => s.CertId == certId).Select(s => s.SectionId).SingleAsync());
    }

    /// <summary>One item with one version and one key.</summary>
    private async Task<(int ItemId, int ItemVersionId)> SeedItemAsync(
        int topicId, string level, string tier, string lifecycle, string tag, string keyLabel = "A")
    {
        await database.SqlAsync($"""
            INSERT INTO Items (Modality, TopicId, CefrLevel, ItemType, ContentTier, LifecycleStatus, BatchTag)
            VALUES ('reading', {topicId}, '{level}', 'sentence_cloze', '{tier}', '{lifecycle}', '{tag}');
            DECLARE @item INT = SCOPE_IDENTITY();
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel, Explanation)
            VALUES (@item, 1, N'Stem {tag}', N'a', N'b', N'c', N'd', '{keyLabel}', N'Why {tag}.');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var version = await db.ItemVersions
            .Where(v => v.Item.BatchTag == tag)
            .Select(v => new { v.ItemId, v.ItemVersionId })
            .SingleAsync();
        return (version.ItemId, version.ItemVersionId);
    }

    /// <summary>
    /// <paramref name="total"/> learners each sit a one-question pool drill at the item's level and
    /// answer it. <paramref name="correct"/> of them get it right; the rest choose
    /// <paramref name="wrongLabel"/>. The session is <c>finished</c> with the correct count the write
    /// path would have left, so nothing about the fixture depends on a counter the service maintains.
    /// </summary>
    private async Task SeedPoolAnswersAsync(
        int certId, int topicId, int versionId, string level, int correct, int total,
        string wrongLabel = "B", string? answerLabel = null)
    {
        for (var i = 0; i < total; i++)
        {
            var isCorrect = i < correct;
            var chosen = answerLabel ?? (isCorrect ? "A" : wrongLabel);
            var correctCount = isCorrect ? 1 : 0;
            var email = $"{Guid.NewGuid():N}@test.local";
            await database.SqlAsync($"""
                INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
                VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Telemetry Learner', {certId});
                DECLARE @user INT = SCOPE_IDENTITY();

                INSERT INTO PracticeSessions (UserId, CertId, TopicId, CefrLevel, Modality, QuestionCount, Status, CorrectCount)
                VALUES (@user, {certId}, {topicId}, '{level}', 'reading', 1, 'finished', {correctCount});
                DECLARE @sess INT = SCOPE_IDENTITY();

                INSERT INTO PracticeAnswers (SessionId, ItemId, ItemVersionId, SelectedLabel, IsCorrect, DisplayOrder)
                VALUES (@sess, (SELECT ItemId FROM ItemVersions WHERE ItemVersionId = {versionId}),
                        {versionId}, '{chosen}', {correctCount}, 1);
                """);
        }
    }

    /// <summary>
    /// Retires every active item in this cell this fixture did not author, so "not drawn" in a
    /// selection assertion means "excluded", not "a neighbour happened to sort first".
    /// </summary>
    private Task RetireNeighboursAsync(int topicId, string level, string tag) =>
        database.SqlAsync($"""
            UPDATE Items SET LifecycleStatus = 'retired'
            WHERE TopicId = {topicId} AND CefrLevel = '{level}' AND LifecycleStatus = 'active'
              AND (BatchTag IS NULL OR BatchTag NOT LIKE '{tag}%');
            """);

    private async Task<int> SeedLearnerAsync(int certId)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Telemetry Learner', {certId});
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    /// <summary>
    /// Two items bound to the certificate's paper, so an attempt answers them through the pool pin.
    /// Returns their version ids in paper order.
    /// </summary>
    private async Task<(int[] VersionIds, int[] QuestionIds)> SeedPaperAsync(
        int certId, int skillId, int examId, int sectionId, int version1, int item1, int version2, int item2)
    {
        await database.SqlAsync($"""
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES ({examId}, {certId}, {sectionId}, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@grp, {examId}, {sectionId}, {skillId}, N'Legacy position one', 1, 'mcq', 1),
                   (@grp, {examId}, {sectionId}, {skillId}, N'Legacy position two', 1, 'mcq', 2);

            INSERT INTO ExamItems (ExamId, SectionId, CertId, ItemVersionId, ItemId, Modality, DisplayOrder) VALUES
              ({examId}, {sectionId}, {certId}, {version1}, {item1}, 'reading', 1),
              ({examId}, {sectionId}, {certId}, {version2}, {item2}, 'reading', 2);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var questions = await db.Questions
            .Where(q => q.ExamId == examId)
            .OrderBy(q => q.DisplayOrder)
            .Select(q => q.QuestionId)
            .ToListAsync();
        return ([version1, version2], questions.ToArray());
    }

    // =============================================================================================
    // AC1 + AC2: a breaching item flips to quarantined and leaves selection immediately.
    // =============================================================================================

    /// <summary>
    /// AC2 in one fixture: an active item whose target-band facility is 1.00 (above the closed ceiling
    /// 0.92) is quarantined by the scheduled evaluation, and the next practice session no longer draws
    /// it.
    /// <para>
    /// A test that only asserted <c>LifecycleStatus == "quarantined"</c> would pass while selection
    /// still served the item — and "removing it from selection immediately" is the learner-facing half
    /// of the claim. So the session is started <em>after</em> the flip and its drawn versions are read.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_facility_breach_flips_the_item_and_takes_it_out_of_selection()
    {
        var certCode = Unique("TMC");
        var (certId, topicId, _, _, _) = await SeedCellAsync(certCode);
        var tag = Unique("fac");
        const string level = "B2";

        var (itemId, versionId) = await SeedItemAsync(topicId, level, "production", "active", tag);
        await SeedPoolAnswersAsync(certId, topicId, versionId, level, correct: 60, total: 60);
        // A healthy control in the same cell. Without it the drill cannot start, and "not drawn" proves
        // only that the cell emptied rather than that the quarantined item was excluded.
        var (_, healthyVersionId) = await SeedItemAsync(topicId, level, "production", "active", $"{tag}-ok");
        await RetireNeighboursAsync(topicId, level, tag);

        var results = await CreateService().EvaluateActiveItemsAsync();

        var outcome = results.Outcomes.Single(r => r.ItemId == itemId);
        Assert.Contains(ItemTelemetry.BreachFacility, outcome.Verdict.Breaches);
        Assert.True(outcome.Verdict.ShouldQuarantine);
        Assert.True(outcome.QuarantinedNow);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            Assert.Equal("quarantined",
                await db.Items.Where(i => i.ItemId == itemId).Select(i => i.LifecycleStatus).SingleAsync());

            var evaluation = await db.ItemTelemetryEvaluations.SingleAsync(e => e.ItemId == itemId);
            // 60 administrations means the 50 checkpoint is the one reached; the row records the sample
            // it was computed over beside the checkpoint it answers
            Assert.Equal(50, evaluation.CheckpointSize);
            Assert.Equal(60, evaluation.AdministrationCount);
            Assert.True(evaluation.BreachFacility);
            Assert.True(evaluation.Quarantined);
            Assert.Equal(1.0m, evaluation.TargetFacility);
            // every answer correct, so the item score has no variance and r does not exist
            Assert.Null(evaluation.Discrimination);
        }

        // AC2's second half: selection no longer draws it, and still draws the healthy control, so the
        // exclusion is the item's lifecycle and not an empty cell
        var learner = await SeedLearnerAsync(certId);
        var practice = CreatePractice();
        var session = await practice.StartSessionAsync(learner, new StartPracticeSessionInput(topicId, CefrLevel.B2));
        var drawn = await practice.GetSessionQuestionsAsync(learner, session.SessionId);

        Assert.Contains(healthyVersionId, drawn.Select(q => q.ItemVersionId));
        Assert.DoesNotContain(versionId, drawn.Select(q => q.ItemVersionId));
    }

    /// <summary>
    /// AC1 at the checkpoint boundary, through the service: 29 answers is not evidence, 30 is. The trap
    /// is an evaluation that runs on every answer — it would quarantine an item on noise, and a test
    /// that only ever seeded a clean sample would never see it.
    /// </summary>
    [Fact]
    public async Task Twenty_nine_answers_is_not_evidence_and_thirty_is()
    {
        var certCode = Unique("TMC");
        var (certId, topicId, _, _, _) = await SeedCellAsync(certCode);
        var service = CreateService();

        var shortTag = Unique("s29");
        var (shortItem, shortVersion) = await SeedItemAsync(topicId, "C1", "production", "active", shortTag);
        // every one of the 29 is wrong: at 30 this is a negative correlation, at 29 it is nothing
        await SeedPoolAnswersAsync(certId, topicId, shortVersion, "C1", correct: 0, total: 29);

        var results = await service.EvaluateActiveItemsAsync();

        var shortOutcome = results.Outcomes.Single(r => r.ItemId == shortItem);
        Assert.Equal(29, shortOutcome.Verdict.Administrations);
        Assert.False(shortOutcome.Verdict.Evaluated);
        Assert.False(shortOutcome.Verdict.ShouldQuarantine);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            Assert.Equal("active",
                await db.Items.Where(i => i.ItemId == shortItem).Select(i => i.LifecycleStatus).SingleAsync());
            Assert.False(await db.ItemTelemetryEvaluations.AnyAsync(e => e.ItemId == shortItem));
        }

        var longTag = Unique("s30");
        var (longItem, longVersion) = await SeedItemAsync(topicId, "C1", "production", "active", longTag);
        await SeedPoolAnswersAsync(certId, topicId, longVersion, "C1", correct: 0, total: 30);

        await service.EvaluateActiveItemsAsync();

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            Assert.Equal("quarantined",
                await db.Items.Where(i => i.ItemId == longItem).Select(i => i.LifecycleStatus).SingleAsync());
            Assert.Equal(30, await db.ItemTelemetryEvaluations
                .Where(e => e.ItemId == longItem)
                .Select(e => e.CheckpointSize)
                .SingleAsync());
        }
    }

    /// <summary>
    /// The schedule is idempotent: a second call at the same administration count writes nothing, flips
    /// nothing twice, and leaves the same single row. The trap is an evaluation that stores a counter
    /// or re-appends evidence — re-running it would then double-count, which is exactly what NFI-05
    /// forbids.
    /// </summary>
    [Fact]
    public async Task Evaluating_the_same_item_twice_changes_nothing()
    {
        var certCode = Unique("TMC");
        var (certId, topicId, _, _, _) = await SeedCellAsync(certCode);
        var tag = Unique("idem");

        var (itemId, versionId) = await SeedItemAsync(topicId, "B1", "production", "active", tag);
        // 26 of 40 correct = 0.65 facility, healthy discrimination: evaluated, not quarantined
        await SeedPoolAnswersAsync(certId, topicId, versionId, "B1", correct: 26, total: 40);

        var service = CreateService();
        await service.EvaluateActiveItemsAsync();

        ItemTelemetryEvaluation first;
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            first = await db.ItemTelemetryEvaluations.AsNoTracking().SingleAsync(e => e.ItemId == itemId);
            Assert.Equal(30, first.CheckpointSize);
            Assert.Equal(40, first.AdministrationCount);
            Assert.False(first.Quarantined);
        }

        var second = await service.EvaluateActiveItemsAsync();

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var rows = await db.ItemTelemetryEvaluations.Where(e => e.ItemId == itemId).ToListAsync();
            Assert.Single(rows);
            Assert.Equal(first.EvaluatedAt, rows[0].EvaluatedAt);
            Assert.Equal("active",
                await db.Items.Where(i => i.ItemId == itemId).Select(i => i.LifecycleStatus).SingleAsync());
        }

        // the second pass reports the item as already evaluated at this checkpoint, so a caller can
        // tell "nothing to do" from "evaluated and clean"
        var repeat = second.Outcomes.Single(r => r.ItemId == itemId);
        Assert.False(repeat.NewlyRecorded);
        Assert.False(repeat.QuarantinedNow);
    }

    /// <summary>
    /// A second run after more answers arrive crosses the next checkpoint and writes the next row,
    /// without rewriting the first. This is what "evaluated when it crosses 30, 50, 100 and 250" means,
    /// and it is the half an "if a row exists, stop" implementation gets wrong.
    /// </summary>
    [Fact]
    public async Task A_later_checkpoint_is_recorded_without_rewriting_the_earlier_one()
    {
        var certCode = Unique("TMC");
        var (certId, topicId, _, _, _) = await SeedCellAsync(certCode);
        var tag = Unique("cps");

        var (itemId, versionId) = await SeedItemAsync(topicId, "B1", "production", "active", tag);
        await SeedPoolAnswersAsync(certId, topicId, versionId, "B1", correct: 23, total: 35);

        var service = CreateService();
        await service.EvaluateActiveItemsAsync();

        await SeedPoolAnswersAsync(certId, topicId, versionId, "B1", correct: 10, total: 20);
        await service.EvaluateActiveItemsAsync();

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var rows = await db.ItemTelemetryEvaluations
            .Where(e => e.ItemId == itemId)
            .OrderBy(e => e.CheckpointSize)
            .Select(e => new { e.CheckpointSize, e.AdministrationCount })
            .ToListAsync();

        Assert.Equal([30, 50], rows.Select(r => r.CheckpointSize));
        // the earlier row still describes the sample it was computed over
        Assert.Equal(35, rows[0].AdministrationCount);
        Assert.Equal(55, rows[1].AdministrationCount);
    }

    /// <summary>
    /// An item already quarantined is not evaluated again and is not flipped twice. The trap: a schedule
    /// that re-evaluates only <c>active</c> items would silently stop reporting on a quarantined one,
    /// and one that ignored the state would try an illegal <c>quarantined -&gt; quarantined</c> write.
    /// </summary>
    [Fact]
    public async Task A_quarantined_item_is_not_evaluated_again()
    {
        var certCode = Unique("TMC");
        var (certId, topicId, _, _, _) = await SeedCellAsync(certCode);
        var tag = Unique("qskip");

        var (itemId, versionId) = await SeedItemAsync(topicId, "B1", "production", "quarantined", tag);
        await SeedPoolAnswersAsync(certId, topicId, versionId, "B1", correct: 0, total: 60);

        var results = await CreateService().EvaluateActiveItemsAsync();

        Assert.DoesNotContain(itemId, results.Outcomes.Select(r => r.ItemId));
        await using var db = DbContextFactory.Create(database.ConnectionString);
        Assert.False(await db.ItemTelemetryEvaluations.AnyAsync(e => e.ItemId == itemId));
    }

    // =============================================================================================
    // AC4 / CNT-45: monotonic learner protection.
    // =============================================================================================

    /// <summary>
    /// A confirmed defect restores a band the learner lost to it. Two items, both keys A. The learner
    /// chose A on the defective item — but its stored key had been wrong, so the attempt scored it
    /// incorrect and the learner finished on 1 of 2 = LOW. Once the true key A is declared, the
    /// recalculation credits the choice and the result becomes 2 of 2 = HIGH.
    /// </summary>
    [Fact]
    public async Task A_confirmed_defect_upgrades_a_learner_who_lost_a_band_to_it()
    {
        var certCode = Unique("TMR");
        var (certId, topicId, skillId, examId, sectionId) = await SeedCellAsync(certCode);
        var tag = Unique("up");

        var (defectiveItem, defectiveVersion) = await SeedItemAsync(topicId, "B1", "production", "active", $"{tag}-bad", "D");
        var (soundItem, soundVersion) = await SeedItemAsync(topicId, "B1", "production", "active", $"{tag}-ok", "A");
        var (versions, questions) = await SeedPaperAsync(certId, skillId, examId, sectionId, defectiveVersion, defectiveItem, soundVersion, soundItem);

        var learner = await SeedLearnerAsync(certId);
        var engine = CreateEngine();
        var attempt = await engine.CreateAttemptAsync(learner, certId, examId, 60);

        // position 1 is the defective item (stored key D, true key A -> choose A and be marked wrong);
        // position 2 is sound (key A -> choose A and be right)
        await engine.SaveAnswerAsync(attempt.AttemptId, learner, questions[0], versions[0] * 10 + 1, false);
        await engine.SaveAnswerAsync(attempt.AttemptId, learner, questions[1], versions[1] * 10 + 1, false);

        var scored = await engine.SubmitAttemptAsync(attempt.AttemptId, learner, isAutoSubmit: false);
        Assert.Equal("LOW", scored.Attempt.BandCode);
        Assert.Equal(25, scored.Attempt.TotalScore);

        var recalculation = await CreateService().RecalculateForDefectiveItemAsync(defectiveItem, trueKeyLabel: "A");

        var affected = Assert.Single(recalculation.Attempts);
        Assert.Equal(attempt.AttemptId, affected.AttemptId);
        Assert.Equal("LOW", affected.PreviousBand);
        Assert.Equal("HIGH", affected.RecalculatedBand);
        Assert.Equal("upgraded", affected.Outcome);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var stored = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
        Assert.Equal("HIGH", stored.BandCode);
        Assert.Equal(50, stored.TotalScore);
    }

    /// <summary>
    /// <b>The rule that cannot be got wrong.</b> This fixture is built so the recalculated score is
    /// strictly <em>worse</em>: the learner answered both items correctly (2 of 2 = 50 = HIGH), and
    /// excluding the defective one leaves 1 of 1 = 25 = LOW on this certificate's scale.
    /// <para>
    /// A test that only checked "a lost band is restored" passes an implementation that writes in both
    /// directions. This one asserts the stored band is unchanged, the outcome is <c>kept</c>, and the
    /// reported recalculation still names the lower band — so the evidence that it <em>would</em> have
    /// downgraded is not lost while the learner is protected.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_recalculation_that_would_lower_a_band_never_downgrades_the_learner()
    {
        var certCode = Unique("TMR");
        var (certId, topicId, skillId, examId, sectionId) = await SeedCellAsync(certCode);
        var tag = Unique("down");

        var (defectiveItem, defectiveVersion) = await SeedItemAsync(topicId, "B1", "production", "active", $"{tag}-bad", "A");
        var (soundItem, soundVersion) = await SeedItemAsync(topicId, "B1", "production", "active", $"{tag}-ok", "A");
        var (versions, questions) = await SeedPaperAsync(certId, skillId, examId, sectionId, defectiveVersion, defectiveItem, soundVersion, soundItem);

        var learner = await SeedLearnerAsync(certId);
        var engine = CreateEngine();
        var attempt = await engine.CreateAttemptAsync(learner, certId, examId, 60);

        // both right: 2 of 2 -> 50 -> HIGH
        await engine.SaveAnswerAsync(attempt.AttemptId, learner, questions[0], versions[0] * 10 + 1, false);
        await engine.SaveAnswerAsync(attempt.AttemptId, learner, questions[1], versions[1] * 10 + 1, false);

        var scored = await engine.SubmitAttemptAsync(attempt.AttemptId, learner, isAutoSubmit: false);
        Assert.Equal("HIGH", scored.Attempt.BandCode);
        Assert.Equal(50, scored.Attempt.TotalScore);

        // excluding the defective item leaves one answered question; getting it right is 1 of 1 = 25
        var recalculation = await CreateService().RecalculateForDefectiveItemAsync(defectiveItem, trueKeyLabel: null);

        var affected = Assert.Single(recalculation.Attempts);
        Assert.Equal("HIGH", affected.PreviousBand);
        Assert.Equal("LOW", affected.RecalculatedBand);   // the recalculation really was lower
        Assert.Equal("kept", affected.Outcome);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var stored = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
        Assert.Equal("HIGH", stored.BandCode);   // unchanged: a system error is not evidence about a learner
        Assert.Equal(50, stored.TotalScore);
    }

    /// <summary>
    /// The same monotonic rule for an attempt that was never graded: nothing has a band to protect and
    /// nothing is invented. The trap is a recalculation that writes a band onto an abandoned or
    /// in-progress attempt because "it references the item".
    /// </summary>
    [Fact]
    public async Task An_attempt_with_no_result_is_not_given_one_by_a_recalculation()
    {
        var certCode = Unique("TMR");
        var (certId, topicId, skillId, examId, sectionId) = await SeedCellAsync(certCode);
        var tag = Unique("noband");

        var (defectiveItem, defectiveVersion) = await SeedItemAsync(topicId, "B1", "production", "active", $"{tag}-bad", "A");
        var (soundItem, soundVersion) = await SeedItemAsync(topicId, "B1", "production", "active", $"{tag}-ok", "A");
        var (versions, _) = await SeedPaperAsync(certId, skillId, examId, sectionId, defectiveVersion, defectiveItem, soundVersion, soundItem);

        var learner = await SeedLearnerAsync(certId);
        var engine = CreateEngine();
        var attempt = await engine.CreateAttemptAsync(learner, certId, examId, 60);

        var recalculation = await CreateService().RecalculateForDefectiveItemAsync(defectiveItem, trueKeyLabel: "A");

        // the in-progress attempt answered nothing, so it is not in the affected set at all
        Assert.DoesNotContain(attempt.AttemptId, recalculation.Attempts.Select(a => a.AttemptId));

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var stored = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
        Assert.Null(stored.BandCode);
        Assert.Null(stored.TotalScore);
    }
}
