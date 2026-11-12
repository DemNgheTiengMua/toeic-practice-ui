using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #154 / #232: a step that cannot be finished makes the whole path a lie. These tests close each
/// activity type through the real service and then re-read the row from a fresh DbContext — "done" has
/// to be persisted, not drawn at read time.
/// <para>
/// #232 moved the match from "which module item" to "which route and which resource", because a drill
/// step names no resource at all: the selector decides its items at run time.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PathStepCompletionTests : IAsyncLifetime
{
    private readonly DatabaseFixture _database;
    private readonly string _dbName = $"EEPath_{Guid.NewGuid():N}"[..24];
    private string _connectionString = null!;

    public PathStepCompletionTests(DatabaseFixture database) => _database = database;

    /// <summary>
    /// <b>#272: a database this class owns.</b> The planner reads the pool to decide which steps a path
    /// can hold, and the closure reads the path's own steps to decide what unlocks next — so a fixture
    /// that stocked one reading cell and wrote one active path shared both with every other class in
    /// the assembly. Measured in a full run: two walk tests failed together while both passed in
    /// isolation, each walking a plan assembled from another class's rows. A private database is the
    /// smallest fix that makes "the plan is the plan this test wrote" true, and it weakens nothing — the
    /// same steps, the same assertions, now actually provable.
    /// </summary>
    public async Task InitializeAsync() => _connectionString = await _database.CreateFreshDatabaseAsync(_dbName);

    public async Task DisposeAsync() => await _database.DropDatabaseAsync(_dbName);

    private CompanionDbContext Db() => DbContextFactory.Create(_connectionString);

    /// <summary>An ad-hoc batch against this class's own database (not the shared fixture's).</summary>
    private async Task SqlAsync(string batch)
    {
        await using var db = Db();
        await db.Database.ExecuteSqlRawAsync(batch);
    }

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private (LearningPathService paths, LearnerService learners, MockExamService mocks, LessonService lessons, CompanionDbContext db) CreateServices()
    {
        var db = Db();
        var learners = new LearnerService(db);
        return (
            new LearningPathService(db, learners),
            learners,
            new MockExamService(db, learners),
            new LessonService(db),
            db);
    }

    /// <summary>
    /// One reading skill at B2 by its own cut-score row, four canonical topics holding two active B2
    /// items each, one lesson with one card, and one published mock exam carrying one pool position.
    /// Every activity type the planner can place is sat-able here.
    ///
    /// The cell is B2 on purpose: `PoolIngestionTests` counts R-DET at B1 exactly, so a fixture that
    /// stocked there would shift a number another test asserts.
    ///
    /// #269: the mock paper is the pool's roster. The position is bound in the reading section, and
    /// <c>LoadMockExamForSkillAsync</c> reads bindings, so the certificate plans its mock step.
    /// </summary>
    private async Task<(int certId, int skillId, int topicId, int mockExamItemId, int examId, int lessonId, int cardId)> SeedCertAsync(string certCode)
    {
        await SqlAsync($"""
            -- #232: the canonical twenty topics are the step coordinate's topic axis.
            INSERT INTO Topics (Modality, Code, Name)
            SELECT v.Modality, v.Code, v.Name
            FROM (VALUES
              ('listening','L-GIST',N'Global gist'), ('listening','L-DET',N'Detail'),
              ('listening','L-INF',N'Inference'), ('listening','L-ATT',N'Attitude'),
              ('listening','L-FUNC',N'Function'), ('listening','L-ORG',N'Organisation'),
              ('reading','R-GIST',N'Global thesis'), ('reading','R-DET',N'Local detail'),
              ('reading','R-INF',N'Bridging inference'), ('reading','R-ATT',N'Authorial stance'),
              ('reading','R-COH',N'Cohesion'), ('reading','R-GRAM',N'Grammar'),
              ('speaking','S-DESC',N'Description'), ('speaking','S-INT',N'Interaction'),
              ('speaking','S-ARG',N'Argument'), ('speaking','S-SYN',N'Synthesis'),
              ('writing','W-CORR',N'Correspondence'), ('writing','W-DATA',N'Data'),
              ('writing','W-ARG',N'Argument'), ('writing','W-DISC',N'Discourse')
            ) v(Modality, Code, Name)
            WHERE NOT EXISTS (SELECT 1 FROM Topics t WHERE t.Modality = v.Modality AND t.Code = v.Code);

            INSERT INTO Certificates (Code, Name, IsActive, ScoreAggregationRule) VALUES ('{certCode}', N'Cert {certCode}', 1, 'avg_whole');
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'READ', N'Reading', 'reading', 1);
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder) VALUES
              (@sR, @cert, 'reading', 'P5', N'Incomplete Sentences', 4, 1, 1);

            UPDATE Skills SET MinScaledScore = 0, MaxScaledScore = 990, ScoreStep = 1 WHERE SkillId = @sR;

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder, CumulativeStudyHours) VALUES
              (@cert, 'A2', N'Elementary',      0, 100, 1, 250),
              (@cert, 'B1', N'Intermediate',  101, 500, 2, 500),
              (@cert, 'B2', N'Upper inter.',  501, 800, 3, 800);

            INSERT INTO ScoreScales (CertId, SkillId, RawScore, ScaledScore) VALUES
              (@cert, @sR, 0, 0),
              (@cert, @sR, 1, 990);

            -- #232: the skill's OWN level, from this certificate's cut-score rows.
            INSERT INTO CefrCutScoreVersions (CertId, TableVersion, ReportsSkillsSeparately, Source, PublishedAt)
            VALUES (@cert, 1, 0, N'Test standard-setting', '2024-01-01');
            INSERT INTO CefrCutScores (CertId, TableVersion, SkillId, CefrLevel, RowKind, MinScaled, MaxScaled) VALUES
              (@cert, 1, @sR, 'A2', 'cut', 0, 100),
              (@cert, 1, @sR, 'B1', 'cut', 101, 500),
              (@cert, 1, @sR, 'B2', 'cut', 501, 990);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
            VALUES (@cert, N'Mock Exam', 'published', 60, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO Lessons (CertId, SkillId, Modality, TopicId, CefrLevel, Title, EstimatedMinutes, DisplayOrder)
            VALUES (@cert, @sR, 'reading',
                    (SELECT TopicId FROM Topics WHERE Modality = 'reading' AND Code = 'R-GRAM'),
                    'B2', N'Grammar Foundations', 8, 1);
            DECLARE @lesson INT = SCOPE_IDENTITY();

            INSERT INTO LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
            VALUES (@lesson, @cert, 'rule', N'Rule', N'Body', 1);
            DECLARE @card INT = SCOPE_IDENTITY();

            -- Two B2 items per canonical reading topic, so every topic is a drillable cell.
            DECLARE @topicId INT, @n INT;
            DECLARE topics_cursor CURSOR LOCAL FAST_FORWARD FOR
              SELECT TopicId FROM Topics WHERE Modality = 'reading' ORDER BY TopicId;
            OPEN topics_cursor;
            FETCH NEXT FROM topics_cursor INTO @topicId;
            WHILE @@FETCH_STATUS = 0
            BEGIN
              SET @n = 1;
              WHILE @n <= 2
              BEGIN
                INSERT INTO Stimuli (CefrLevel, Passage) VALUES ('B2', N'Passage');
                INSERT INTO Items (StimulusId, Modality, TopicId, CefrLevel, ItemType, PlacementEligible, ContentTier, LifecycleStatus)
                VALUES (SCOPE_IDENTITY(), 'reading', @topicId, 'B2', 'passage_comprehension', 0, 'demo', 'active');
                DECLARE @itemId INT = SCOPE_IDENTITY();
                INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
                VALUES (@itemId, 1, N'Stem', N'A', N'B', N'C', N'D', 'A');
                SET @n = @n + 1;
              END
              FETCH NEXT FROM topics_cursor INTO @topicId;
            END
            CLOSE topics_cursor; DEALLOCATE topics_cursor;
            """);

        await using var db = DbContextFactory.Create(_connectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var skillId = await db.Skills.Where(s => s.CertId == cert.CertId).Select(s => s.SkillId).SingleAsync();
        var topicId = await db.Topics.Where(t => t.Modality == "reading" && t.Code == "R-DET").Select(t => t.TopicId).SingleAsync();
        var examId = await db.Exams.Where(e => e.CertId == cert.CertId && e.Purpose == "mock").Select(e => e.ExamId).SingleAsync();
        var lessonId = await db.Lessons.Where(l => l.CertId == cert.CertId).Select(l => l.LessonId).SingleAsync();
        var cardId = await db.LessonCards.Where(c => c.LessonId == lessonId).Select(c => c.CardId).SingleAsync();

        // #269: the paper is a pool position bound in the reading section. The id a test addresses is
        // its ExamItemId, and the answer writes the pinned version's SelectedLabel.
        var sectionId = await db.Sections.Where(s => s.CertId == cert.CertId).Select(s => s.SectionId).SingleAsync();
        var position = await PoolFixture.AddMcqAsync(
            db, examId, cert.CertId, sectionId, "reading", "Mock position", displayOrder: 1);

        return (cert.CertId, skillId, topicId, position.ExamItemId, examId, lessonId, cardId);
    }

    private async Task<int> SeedLearnerAsync(int certId, decimal weeklyHours = 10m)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Test Learner', {certId});
            DECLARE @u INT = SCOPE_IDENTITY();
            INSERT INTO TargetBands (UserId, CertId, TargetBandCode, CurrentBandCode, TargetDate, WeeklyStudyHours, FeasibilityStatus)
            VALUES (@u, {certId}, 'B2', 'B1', DATEADD(DAY, 210, CAST(SYSDATETIMEOFFSET() AS DATE)), {weeklyHours}, 'feasible');

            -- #232: a measured level, so the step's level is read from this skill's own cut row rather
            -- than from the stocked cell. The pool is shared across the assembly, so a fallback-driven
            -- level would depend on which other tests ran first.
            DECLARE @measSkill INT = (SELECT TOP 1 SkillId FROM Skills WHERE CertId = {certId} ORDER BY SkillId);
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
            VALUES ({certId}, N'Measurement sitting {Guid.NewGuid():N}', 'published', 60, 'mock');
            DECLARE @measExam INT = SCOPE_IDENTITY();
            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, SubmittedAt, TotalScore, BandCode, WrittenByService, ProjectionVersion)
            VALUES (@u, @measExam, {certId}, 'graded',
                    DATEADD(DAY, -1, SYSDATETIMEOFFSET()), DATEADD(DAY, -1, SYSDATETIMEOFFSET()),
                    DATEADD(DAY, -1, SYSDATETIMEOFFSET()), 700, 'B2', 'AttemptEngine', 1);
            DECLARE @measAttempt INT = SCOPE_IDENTITY();
            INSERT INTO AttemptSkillScores (AttemptId, SkillId, CertId, RawScore, ScaledScore)
            VALUES (@measAttempt, @measSkill, {certId}, 18, 700);
            """);

        await using var db = DbContextFactory.Create(_connectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    /// <summary>Ten credits so a mock exam can be started (one credit a sitting).</summary>
    private async Task GrantCreditsAsync(int userId, int credits)
    {
        await SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
              INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), '{Guid.NewGuid():N}@admin.test', 0x01, N'Admin');
            DECLARE @admin INT = SCOPE_IDENTITY();
            INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
            VALUES ({userId}, 'admin_grant', {credits}, @admin, N'Test credits');
            """);
    }

    private static async Task<List<LearnerPathStep>> StepsAsync(CompanionDbContext db, int learnerPathId) =>
        await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == learnerPathId)
            .OrderBy(s => s.WeekNumber)
            .ThenBy(s => s.WeekSequence)
            .ThenBy(s => s.StepId)
            .ToListAsync();

    /// <summary>
    /// Does the activity a step plans, through the real service, in the way a learner would. The step's
    /// type decides the route; a drill step closes through its practice session, which the real learner
    /// answers.
    /// <para>
    /// #269: the mock paper's one position is a pool binding, so it is answered with the option id the
    /// sitting renders for the version's own key label — the id a pool answer is addressed by.
    /// </para>
    /// </summary>
    private async Task SitStepAsync(
        int userId, LearnerPathStep step, MockExamService mocks, LessonService lessons,
        LearningPathService paths, PracticeService practice, int mockExamItemId, int lessonId, int cardId, CompanionDbContext db)
    {
        switch (step.StepType)
        {
            case "mock_section":
            {
                var attempt = await mocks.StartAttemptAsync(userId, step.RefExamId!.Value);
                await AnswerPoolMockPositionAsync(
                    userId, attempt.AttemptId, step.RefExamId.Value, mockExamItemId, mocks, db);
                await mocks.SubmitAttemptAsync(userId, attempt.AttemptId, isAutoSubmit: false);
                break;
            }
            case "concept_lesson":
            case "expanded_remedial":
                await lessons.RecordCardViewAsync(userId, lessonId, cardId);
                break;
            default:
                // #160: there is no manual "mark done" route. A drill is closed by answering its
                // session, which is what the real learner does.
                await SitDrillAsync(userId, step, paths, practice, db);
                break;
        }
    }

    /// <summary>
    /// Answers one pool position of the mock paper with its key. The option id comes from the projection
    /// the sitting renders — <c>PoolFixture.OptionIdAsync</c> reads it off the take view — so the answer
    /// is addressed the way the browser addresses it, not by a recomputed id.
    /// </summary>
    private static async Task AnswerPoolMockPositionAsync(
        int userId, int attemptId, int examId, int examItemId, MockExamService mocks, CompanionDbContext db)
    {
        var keyLabel = await db.ExamItems.AsNoTracking()
            .Where(i => i.ExamItemId == examItemId)
            .Select(i => i.ItemVersion.KeyLabel)
            .SingleAsync();
        var correct = await PoolFixture.OptionIdAsync(db, examId, examItemId, keyLabel!);

        await mocks.SaveAnswerAsync(userId, attemptId, examItemId, correct, isFlagged: false);
    }

    /// <summary>Opens a drill step through the service and answers every item it drew.</summary>
    private async Task SitDrillAsync(
        int userId, LearnerPathStep step, LearningPathService paths, PracticeService practice, CompanionDbContext db)
    {
        // A step closed directly by an earlier test in this file may already be open.
        await db.Entry(step).ReloadAsync();
        if (step.Status == "done")
        {
            return;
        }

        if (step.Status == "locked")
        {
            await SqlAsync($"UPDATE LearnerPathSteps SET Status = 'available' WHERE StepId = {step.StepId};");
            await db.Entry(step).ReloadAsync();
        }

        var start = await paths.StartStepAsync(userId, step.StepId);
        var sessionId = start.SessionId!.Value;

        while (true)
        {
            var take = await practice.GetSessionTakeAsync(userId, sessionId);
            var unanswered = take.Questions.FirstOrDefault(q => !q.SelectedOptionId.HasValue && !q.IsCorrect.HasValue);
            if (unanswered is null)
            {
                return;
            }

            var version = await db.ItemVersions.AsNoTracking()
                .FirstAsync(v => v.ItemVersionId == unanswered.ItemVersionId);
            var keyIndex = version.KeyLabel?.Trim().ToUpperInvariant() switch
            {
                "A" => 0, "B" => 1, "C" => 2, "D" => 3, _ => -1,
            };
            var keyOptionId = unanswered.ItemVersionId!.Value * 10 + keyIndex + 1;
            var chosen = unanswered.Options.First(o => o.OptionId == keyOptionId);

            // #187: a pool answer is addressed by the version it pinned.
            await practice.SubmitAnswerAsync(
                userId, sessionId, unanswered.ItemVersionId.Value,
                new SubmitPracticeAnswerInput(SelectedOptionId: chosen.OptionId));
        }
    }

    [Fact]
    public async Task Sitting_a_mock_exam_marks_its_path_step_done_and_unlocks_the_next()
    {
        var certCode = Unique("MOCK");
        var (certId, _, _, mockExamItemId, examId, lessonId, cardId) = await SeedCertAsync(certCode);
        var userId = await SeedLearnerAsync(certId);
        await GrantCreditsAsync(userId, 5);

        var (paths, learners, mocks, lessons, db) = CreateServices();
        var practice = new PracticeService(db, learners);
        var summary = await paths.GeneratePathAsync(userId);
        var steps = await StepsAsync(db, summary.LearnerPathId!.Value);

        var mockStep = steps.First(s => s.RefExamId == examId);
        Assert.NotEqual("done", mockStep.Status);

        // Walk the earlier steps so the mock step is the one open when the exam is sat.
        foreach (var earlier in steps.Where(s => s.StepId < mockStep.StepId))
        {
            await db.Entry(earlier).ReloadAsync();
            Assert.NotEqual("locked", earlier.Status);
            await SitStepAsync(userId, earlier, mocks, lessons, paths, practice, mockExamItemId, lessonId, cardId, db);
            await db.Entry(earlier).ReloadAsync();
            Assert.Equal("done", earlier.Status);
        }

        // Sit and submit the mock exam through the real service.
        var attempt = await mocks.StartAttemptAsync(userId, examId);
        await AnswerPoolMockPositionAsync(userId, attempt.AttemptId, examId, mockExamItemId, mocks, db);
        await mocks.SubmitAttemptAsync(userId, attempt.AttemptId, isAutoSubmit: false);

        // Re-read from a fresh context: `done` has to be in the row, not drawn by a read path.
        await using var verifyDb = DbContextFactory.Create(_connectionString);
        var persisted = await verifyDb.LearnerPathSteps.SingleAsync(s => s.StepId == mockStep.StepId);

        Assert.Equal("done", persisted.Status);
        Assert.NotNull(persisted.CompletedAt);
        Assert.NotNull(persisted.Score);

        // And the step after it is open.
        var nextStep = await verifyDb.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId && s.StepId > mockStep.StepId)
            .OrderBy(s => s.WeekNumber)
            .ThenBy(s => s.WeekSequence)
            .FirstOrDefaultAsync();

        if (nextStep is not null)
        {
            Assert.NotEqual("locked", nextStep.Status);
        }
    }

    [Fact]
    public async Task Finishing_a_lesson_marks_its_path_step_done_and_the_row_survives_a_reload()
    {
        var certCode = Unique("LESSON");
        var (certId, _, _, _, _, lessonId, cardId) = await SeedCertAsync(certCode);
        var userId = await SeedLearnerAsync(certId);

        var (paths, _, _, lessons, db) = CreateServices();
        var summary = await paths.GeneratePathAsync(userId);
        var steps = await StepsAsync(db, summary.LearnerPathId!.Value);

        var lessonStep = steps.First(s => s.RefLessonId == lessonId);

        // Walk the steps before it so the lesson is open.
        foreach (var earlier in steps.TakeWhile(s => s.StepId < lessonStep.StepId))
        {
            await db.Entry(earlier).ReloadAsync();
            await PathStepCompletion.CloseStepAsync(db, earlier, score: 90m, accuracyPct: 90m);
        }

        // Read the only card — that is what completes the lesson.
        await lessons.RecordCardViewAsync(userId, lessonId, cardId);

        await using var verifyDb = DbContextFactory.Create(_connectionString);
        var persisted = await verifyDb.LearnerPathSteps.SingleAsync(s => s.StepId == lessonStep.StepId);

        Assert.Equal("done", persisted.Status);
        Assert.NotNull(persisted.CompletedAt);
        Assert.Null(persisted.Score);
    }

    [Fact]
    public async Task A_finished_lesson_written_by_hand_still_shows_done_after_a_reload_of_the_path_screen()
    {
        var certCode = Unique("RELOAD");
        var (certId, _, _, _, _, lessonId, cardId) = await SeedCertAsync(certCode);
        var userId = await SeedLearnerAsync(certId);

        var (paths, _, _, lessons, db) = CreateServices();
        var summary = await paths.GeneratePathAsync(userId);
        var steps = await StepsAsync(db, summary.LearnerPathId!.Value);
        var lessonStep = steps.First(s => s.RefLessonId == lessonId);

        await PathStepCompletion.CloseStepAsync(
            db, steps.FirstOrDefault(s => s.StepId < lessonStep.StepId) ?? steps[1], score: 90m, accuracyPct: 90m);

        // Force the lesson step open, then finish the lesson.
        var step = await db.LearnerPathSteps.SingleAsync(s => s.StepId == lessonStep.StepId);
        step.Status = "available";
        await db.SaveChangesAsync();

        await lessons.RecordCardViewAsync(userId, lessonId, cardId);

        // A new service instance (new DbContext) — the state has to come from the database.
        var (freshPaths, _, _, _, _) = CreateServices();
        var reread = await freshPaths.GetActivePathAsync(userId);
        var rereadStep = await FreshStepAsync(lessonStep.StepId);

        Assert.Equal("done", rereadStep.Status);
        Assert.True(reread.HasActivePath);
    }

    private async Task<LearnerPathStep> FreshStepAsync(int stepId)
    {
        await using var db = DbContextFactory.Create(_connectionString);
        return await db.LearnerPathSteps.AsNoTracking().SingleAsync(s => s.StepId == stepId);
    }

    [Fact]
    public async Task Completing_every_step_in_plan_order_reaches_the_last_step_and_completes_the_path()
    {
        // The gate: no step may sit unreachable forever. Walk the whole path, mock exams included,
        // in the order the plan wrote it.
        var certCode = Unique("WALK");
        var (certId, _, _, mockExamItemId, _, lessonId, cardId) = await SeedCertAsync(certCode);
        var userId = await SeedLearnerAsync(certId);
        await GrantCreditsAsync(userId, 20);

        var (paths, learners, mocks, lessons, db) = CreateServices();
        var practice = new PracticeService(db, learners);
        var summary = await paths.GeneratePathAsync(userId);

        var steps = await StepsAsync(db, summary.LearnerPathId!.Value);
        // #234: the old floor of 3 steps × 4 weeks is gone. The invariant the walk needs is that there is
        // a plan to walk at all.
        Assert.NotEmpty(steps);

        foreach (var step in steps)
        {
            await db.Entry(step).ReloadAsync();
            Assert.NotEqual("locked", step.Status);   // it must already be open by now

            await SitStepAsync(userId, step, mocks, lessons, paths, practice, mockExamItemId, lessonId, cardId, db);

            var closed = await FreshStepAsync(step.StepId);
            Assert.Equal("done", closed.Status);
        }

        var path = await db.LearnerPaths.AsNoTracking().SingleAsync(p => p.LearnerPathId == summary.LearnerPathId);
        Assert.Equal("completed", path.Status);
    }

    [Fact]
    public async Task A_drill_step_records_its_measured_accuracy()
    {
        var certCode = Unique("ACC");
        var (certId, _, _, _, _, _, _) = await SeedCertAsync(certCode);
        var userId = await SeedLearnerAsync(certId);

        var (paths, learners, _, _, db) = CreateServices();
        var practice = new PracticeService(db, learners);
        var summary = await paths.GeneratePathAsync(userId);
        var steps = await StepsAsync(db, summary.LearnerPathId!.Value);
        var drillStep = steps.First(s => s.StepType == "focus_practice");

        await SqlAsync($"UPDATE LearnerPathSteps SET Status = 'available' WHERE StepId = {drillStep.StepId};");
        await db.Entry(drillStep).ReloadAsync();

        var start = await paths.StartStepAsync(userId, drillStep.StepId);
        var sessionId = start.SessionId!.Value;

        // Answer every item right: 100% accuracy on the step.
        while (true)
        {
            var take = await practice.GetSessionTakeAsync(userId, sessionId);
            var unanswered = take.Questions.FirstOrDefault(q => !q.SelectedOptionId.HasValue && !q.IsCorrect.HasValue);
            if (unanswered is null)
            {
                break;
            }

            var right = unanswered.Options.FirstOrDefault(o => o.OptionId == unanswered.CorrectOptionId)
                        ?? unanswered.Options[0];
            await practice.SubmitAnswerAsync(
                userId, sessionId, unanswered.ItemVersionId!.Value,
                new SubmitPracticeAnswerInput(SelectedOptionId: right.OptionId));
        }

        var closed = await FreshStepAsync(drillStep.StepId);
        Assert.Equal("done", closed.Status);
        Assert.Equal(100m, closed.ActualAccuracyPct);
    }

    [Fact]
    public async Task Closing_a_planned_step_marks_it_done_with_its_score_and_unlocks_the_next()
    {
        var certCode = Unique("CLOSE");
        var (certId, _, _, _, _, _, _) = await SeedCertAsync(certCode);
        var userId = await SeedLearnerAsync(certId);

        var (paths, _, _, _, db) = CreateServices();
        var summary = await paths.GeneratePathAsync(userId);
        var steps = await StepsAsync(db, summary.LearnerPathId!.Value);

        var first = steps[0];
        var second = steps[1];
        Assert.Equal("available", first.Status);
        Assert.Equal("locked", second.Status);

        await PathStepCompletion.CloseStepAsync(db, first, score: 90m, accuracyPct: 90m);

        await db.Entry(first).ReloadAsync();
        await db.Entry(second).ReloadAsync();
        Assert.Equal("done", first.Status);
        Assert.Equal(90m, first.Score);
        Assert.NotNull(first.CompletedAt);
        Assert.Equal("available", second.Status);
    }

    [Fact]
    public async Task A_step_on_a_reset_path_is_not_reopened_by_a_late_activity()
    {
        // Found by review: CloseStepAsync took a bare step and wrote "done" without reading the path.
        // PracticeService calls it from a session that can still be open when the learner resets the
        // path, so a late answer marked the reset path's step done and UnlockNextAsync could then
        // flip the whole path to "completed" - a path the learner never walked.
        var certCode = Unique("RST");
        var (certId, _, _, _, _, _, _) = await SeedCertAsync(certCode);
        var userId = await SeedLearnerAsync(certId);
        await GrantCreditsAsync(userId, 20);

        var (paths, _, _, _, db) = CreateServices();
        var summary = await paths.GeneratePathAsync(userId);
        var steps = await StepsAsync(db, summary.LearnerPathId!.Value);

        // The learner abandons this path; its steps stay exactly where they are.
        await SqlAsync($"""
            UPDATE LearnerPaths SET Status = 'reset' WHERE LearnerPathId = {summary.LearnerPathId};
            """);

        var first = steps[0];
        await PathStepCompletion.CloseStepAsync(db, first, score: 90m, accuracyPct: 90m);

        await db.Entry(first).ReloadAsync();
        Assert.NotEqual("done", first.Status);
        Assert.Null(first.Score);

        await using var reread = DbContextFactory.Create(_connectionString);
        var path = await reread.LearnerPaths.AsNoTracking()
            .SingleAsync(p => p.LearnerPathId == summary.LearnerPathId);
        Assert.Equal("reset", path.Status);
    }

    [Fact]
    public async Task Closing_a_step_twice_keeps_the_first_score()
    {
        // Idempotence: two activities can finish close together. The second close must not overwrite
        // the score the first one recorded, nor run the unlock a second time.
        var certCode = Unique("IDEM");
        var (certId, _, _, _, _, _, _) = await SeedCertAsync(certCode);
        var userId = await SeedLearnerAsync(certId);
        await GrantCreditsAsync(userId, 20);

        var (paths, _, _, _, db) = CreateServices();
        var summary = await paths.GeneratePathAsync(userId);
        var steps = await StepsAsync(db, summary.LearnerPathId!.Value);

        var first = steps[0];
        await PathStepCompletion.CloseStepAsync(db, first, score: 80m, accuracyPct: 80m);
        await PathStepCompletion.CloseStepAsync(db, first, score: 20m, accuracyPct: 20m);

        await db.Entry(first).ReloadAsync();
        Assert.Equal("done", first.Status);
        Assert.Equal(80m, first.Score);
    }
}
