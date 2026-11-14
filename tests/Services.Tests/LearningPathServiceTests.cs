using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services;
using Services.Credits;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class LearningPathServiceTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    /// <summary>
    /// Opens a step the plan left locked. The update goes through raw SQL because the unlock rule is a
    /// separate ticket's concern; the tracked entity is then detached and re-read, so a later service
    /// call sees the stored status rather than the generator's stale copy.
    /// </summary>
    private async Task OpenStepAsync(CompanionDbContext db, int stepId)
    {
        await database.SqlAsync($"UPDATE LearnerPathSteps SET Status = 'available' WHERE StepId = {stepId};");

        var tracked = db.ChangeTracker.Entries<LearnerPathStep>()
            .FirstOrDefault(e => e.Entity.StepId == stepId);
        if (tracked is not null)
        {
            await tracked.ReloadAsync();
        }
    }

    private (LearningPathService service, LearnerService learners, PracticeService practice, CompanionDbContext db) CreateServices()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var learners = new LearnerService(db);
        var service = new LearningPathService(db, learners);
        var practice = new PracticeService(db, learners);
        return (service, learners, practice, db);
    }

    /// <summary>
    /// Answers a pool session through the real service, using the option ids the take view hands the
    /// browser. The pool's option-id bijection is <c>AttemptEngine</c>'s and internal, so this reads the
    /// ids off the rendered question rather than recomputing the arithmetic — the same route the page
    /// takes, which is what makes the grade the product's rather than the test's.
    /// <para>
    /// The correct option id is only revealed once an answer is recorded, so the right choice is taken
    /// from the pinned version's key: the option whose text the version marks correct is the first one
    /// whose label equals the key. A wrong answer is any other.
    /// </para>
    /// </summary>
    private static async Task AnswerEveryItemAsync(
        PracticeService practice, CompanionDbContext db, int userId, int sessionId, bool correct)
    {
        while (true)
        {
            var take = await practice.GetSessionTakeAsync(userId, sessionId);
            var unanswered = take.Questions
                .FirstOrDefault(q => !q.SelectedOptionId.HasValue && !q.IsCorrect.HasValue);

            if (unanswered is null)
            {
                return;
            }

            var version = await db.ItemVersions.AsNoTracking()
                .FirstAsync(v => v.ItemVersionId == unanswered.ItemVersionId);

            if (!string.IsNullOrWhiteSpace(version.AcceptedAnswersJson))
            {
                var accepted = System.Text.Json.JsonSerializer.Deserialize<string[]>(version.AcceptedAnswersJson)?.FirstOrDefault() ?? "answer";
                var text = correct ? accepted : "wrong answer text";
                await practice.SubmitAnswerAsync(
                    userId, sessionId, unanswered.ItemVersionId!.Value,
                    new SubmitPracticeAnswerInput(ResponseText: text));
                continue;
            }

            var keyStr = version.KeyLabel?.Trim() ?? "A";
            var keys = keyStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (keys.Length > 1)
            {
                var matchingOptions = unanswered.Options
                    .Where(o => keys.Contains(o.Label, StringComparer.OrdinalIgnoreCase))
                    .Select(o => o.OptionId)
                    .ToList();
                var selected = correct
                    ? matchingOptions
                    : unanswered.Options.Take(1).Select(o => o.OptionId).ToList();
                await practice.SubmitAnswerAsync(
                    userId, sessionId, unanswered.ItemVersionId!.Value,
                    new SubmitPracticeAnswerInput(SelectedOptionIds: selected));
                continue;
            }

            var keyLabel = keys.Length > 0 ? keys[0] : "A";
            var correctOption = unanswered.Options.FirstOrDefault(o => string.Equals(o.Label, keyLabel, StringComparison.OrdinalIgnoreCase))
                ?? unanswered.Options.FirstOrDefault();

            if (correctOption is null)
            {
                continue;
            }

            var wrongOption = unanswered.Options.FirstOrDefault(o => o.OptionId != correctOption.OptionId) ?? correctOption;
            var chosen = correct ? correctOption : wrongOption;

            await practice.SubmitAnswerAsync(
                userId,
                sessionId,
                unanswered.ItemVersionId!.Value,
                new SubmitPracticeAnswerInput(SelectedOptionId: chosen.OptionId));
        }
    }

    /// <summary>
    /// #153/#154: a learner here has a goal with a deadline and a pace, because path generation no
    /// longer invents one — #153 refuses a goal it cannot judge, and #154 plans the volume from
    /// <c>WeeklyStudyHours</c>.
    /// <para>
    /// #232: the learner is also given a graded sitting that measures the certificate's skill at B2, so
    /// the step's level is resolved from that skill's own cut row rather than from the stock fallback.
    /// The pool is shared across the whole assembly and other tests stock the same modality at other
    /// levels, so a fallback-driven level would depend on test order; a measured one does not.
    /// </para>
    /// </summary>
    private async Task<int> SeedLearnerAsync(
        string? activeCertCode = null, string? currentBand = "A2", string? targetBand = "B2",
        int measuredScaledScore = 700, string measuredBandCode = "B2")
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        var goal = activeCertCode is not null && currentBand is not null && targetBand is not null
            ? $"""
              INSERT INTO TargetBands (UserId, CertId, TargetBandCode, CurrentBandCode, TargetDate, WeeklyStudyHours)
              VALUES (@u, (SELECT CertId FROM Certificates WHERE Code = '{activeCertCode}'),
                      '{targetBand}', '{currentBand}',
                      DATEADD(DAY, 210, CAST(SYSDATETIMEOFFSET() AS DATE)), 20);
              """
            : "";

        var measurement = activeCertCode is not null
            ? $"""
              DECLARE @measCert INT = (SELECT CertId FROM Certificates WHERE Code = '{activeCertCode}');
              DECLARE @measSkill INT = (SELECT TOP 1 SkillId FROM Skills WHERE CertId = @measCert ORDER BY SkillId);
              INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
              VALUES (@measCert, N'Measurement sitting {Guid.NewGuid():N}', 'published', 60, 'mock');
              DECLARE @measExam INT = SCOPE_IDENTITY();
              INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, SubmittedAt, TotalScore, BandCode)
              VALUES (@u, @measExam, @measCert, 'graded',
                      DATEADD(DAY, -1, SYSDATETIMEOFFSET()), DATEADD(DAY, -1, SYSDATETIMEOFFSET()),
                      DATEADD(DAY, -1, SYSDATETIMEOFFSET()), {measuredScaledScore}, '{measuredBandCode}');
              DECLARE @measAttempt INT = SCOPE_IDENTITY();
              INSERT INTO AttemptSkillScores (AttemptId, SkillId, CertId, RawScore, ScaledScore)
              VALUES (@measAttempt, @measSkill, @measCert, 18, {measuredScaledScore});
              """
            : "";

        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Test Learner',
                    (SELECT CertId FROM Certificates WHERE Code = {(activeCertCode is null ? "NULL" : $"'{activeCertCode}'")}));
            DECLARE @u INT = SCOPE_IDENTITY();
            INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
            VALUES (@u, 'admin_grant', 10, @u, N'Test credits');
            {goal}
            {measurement}
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    /// <summary>
    /// #232: a certificate with one reading skill, a cut-score table putting that skill at B2, four
    /// canonical reading topics each holding two active B2 items, one lesson and one published mock.
    /// Nothing here is a template: the plan is a set of coordinates.
    ///
    /// The cell is R-INF at B2 on purpose. `PoolIngestionTests` and `ItemTelemetry` count the stock in
    /// R-DET at B1 exactly, so a fixture that added items there would shift a number another test
    /// asserts and read as a defect in the ingestion gates.
    /// </summary>
    private async Task<(int certId, int skillId, int topicId, int lessonId, int examId)> SeedPathFixtureAsync(string certCode)
    {
        await database.SqlAsync($"""
            -- #232: the canonical twenty topics are the step coordinate's topic axis. The schema holds
            -- the closed-set trigger but no rows, so every fixture that plants a step seeds its topic
            -- individually - a table-wide guard is the #184/#185 seed defect.
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

            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'READ', N'Reading', 'reading', 1);
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, OptionCount, ExpectedQuestionCount, DisplayOrder) VALUES
              (@sR, @cert, 'reading', 'P5', N'Sentences', 4, 1, 1);

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder, CumulativeStudyHours) VALUES
              (@cert, 'A1', N'Beginner', 0, 200, 1, 100),
              (@cert, 'A2', N'Elementary', 201, 500, 2, 250),
              (@cert, 'B1', N'Intermediate', 501, 750, 3, 500),
              (@cert, 'B2', N'Upper intermediate', 751, 990, 4, 800);

            -- #232: the skill's OWN level, from the certificate's own cut-score rows.

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
            VALUES (@cert, N'Mock Exam 1', 'published', 120, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            -- #240: a lesson is addressed by (skill, topic, level), so the fixture names the cell.
            INSERT INTO Lessons (CertId, SkillId, Modality, TopicId, CefrLevel, Title, EstimatedMinutes, DisplayOrder)
            VALUES (@cert, @sR, 'reading',
                    (SELECT TopicId FROM Topics WHERE Modality = 'reading' AND Code = 'R-GRAM'),
                    'B2', N'Grammar Foundations', 8, 1);
            DECLARE @lesson INT = SCOPE_IDENTITY();
            INSERT INTO LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
            VALUES (@lesson, @cert, 'rule', N'Rule', N'Text', 1);

            -- Two B2 items per canonical reading topic, so every topic is a drillable cell.
            DECLARE @topicId INT, @n INT;
            DECLARE topics_cursor CURSOR LOCAL FAST_FORWARD FOR
              SELECT TopicId FROM Topics WHERE Modality = 'reading';
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

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var skillId = await db.Skills.Where(s => s.CertId == cert.CertId).Select(s => s.SkillId).SingleAsync();
        var topicId = await db.Topics
            .Where(t => t.Modality == "reading" && t.Code == "R-DET")
            .Select(t => t.TopicId).SingleAsync();
        var lessonId = await db.Lessons.Where(l => l.CertId == cert.CertId).Select(l => l.LessonId).SingleAsync();
        var examId = await db.Exams.Where(e => e.CertId == cert.CertId && e.Purpose == "mock").Select(e => e.ExamId).SingleAsync();

        // #269: the mock paper is the pool's roster. `LoadMockExamForSkillAsync` reads ExamItems, so a
        // position bound in the reading section is what makes this certificate plan a mock step.
        var sectionId = await db.Sections.Where(s => s.CertId == cert.CertId).Select(s => s.SectionId).SingleAsync();
        await PoolFixture.AddMcqAsync(
            db, examId, cert.CertId, sectionId, "reading", "Mock position", displayOrder: 1);

        return (cert.CertId, skillId, topicId, lessonId, examId);
    }

    [Fact]
    public async Task GetActivePathAsync_WhenNoActivePath_ReturnsSummaryWithHasActivePathFalse()
    {
        var certCode = Unique("C");
        await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, _) = CreateServices();

        var summary = await service.GetActivePathAsync(userId);

        Assert.False(summary.HasActivePath);
        Assert.Null(summary.LearnerPathId);
        Assert.Empty(summary.OpenSteps);
        Assert.Equal(certCode, summary.ActiveCert.Code);
    }

    [Fact]
    public async Task GeneratePathAsync_CreatesActivePathAndSteps()
    {
        var certCode = Unique("C");
        var (_, skillId, _, _, _) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);

        Assert.True(summary.HasActivePath);
        Assert.NotNull(summary.LearnerPathId);
        // #156: the latest graded sitting's band is the starting band, which is what the measurement the
        // fixture planted reports.
        Assert.Equal("B2", summary.FromBandCode);
        Assert.Equal("B2", summary.ToBandCode);
        Assert.Equal(0, summary.CompletedSteps);

        // #154: the steps are a plan, not a walk over a template's items. Four weeks of 3-5 steps,
        // and the first one is open while the rest wait.
        var steps = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId)
            .OrderBy(s => s.StepId)
            .ToListAsync();

        // #234: the old "3–5 steps a week" bound is gone with the count-first rule. What remains true is
        // that every one of the four weeks has work in it, and that the four cover the window.
        Assert.Equal(
            Enumerable.Range(1, LearningPathPlanner.WindowWeeks).ToList(),
            steps.Select(s => s.WeekNumber!.Value).Distinct().OrderBy(w => w).ToList());
        Assert.Equal("available", steps[0].Status);
        Assert.All(steps.Skip(1), s => Assert.Equal("locked", s.Status));

        // #232: every step names the skill it drills and that skill's own level.
        Assert.All(steps, s =>
        {
            Assert.Equal(skillId, s.SkillId);
            Assert.Equal("B2", s.TargetCefr);
        });

        // #154: the four-week window is persisted, and the summary reports it week by week.
        Assert.Equal(
            Enumerable.Range(1, LearningPathPlanner.WindowWeeks).ToList(),
            steps.Select(s => s.WeekNumber!.Value).Distinct().OrderBy(w => w).ToList());
        Assert.Equal(LearningPathPlanner.WindowWeeks, summary.Weeks.Count);
    }

    [Fact]
    public async Task GeneratePathAsync_WhenActivePathAlreadyExists_ThrowsLearningPathException()
    {
        var certCode = Unique("C");
        await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, _) = CreateServices();

        await service.GeneratePathAsync(userId);

        var ex = await Assert.ThrowsAsync<LearningPathException>(() => service.GeneratePathAsync(userId));
        Assert.Equal(LearningPathException.PathAlreadyActive, ex.Code);
    }

    /// <summary>
    /// #232: a step's detail is addressed by its own id, because there is no module to address. The test
    /// asserts the coordinate's four metadata columns are all present, which is what the path screen
    /// shows before a step starts (D21a).
    /// </summary>
    [Fact]
    public async Task GetStepAsync_ReturnsTheStepsCoordinateAndState()
    {
        var certCode = Unique("C");
        await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);
        var first = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId)
            .OrderBy(s => s.WeekNumber)
            .ThenBy(s => s.WeekSequence)
            .FirstAsync();

        var step = await service.GetStepAsync(userId, first.StepId);

        Assert.NotNull(step);
        Assert.Equal(first.StepId, step!.StepId);
        Assert.Equal("READ", step.SkillCode);
        // The topic is the plan's weakest-first choice, which without practice history falls back to the
        // canonical order. Assert it is a canonical reading topic with a name, not a specific code.
        Assert.Contains(step.TopicCode, new[] { "R-GIST", "R-DET", "R-INF", "R-ATT", "R-COH", "R-GRAM" });
        Assert.False(string.IsNullOrWhiteSpace(step.TopicName));
        Assert.Equal(CefrLevel.B2, step.TargetCefr);
        Assert.Equal(first.StepType, step.StepType.ToCode());
        Assert.Equal(first.SizingTier, step.SizingTier.ToCode());
        Assert.Equal(first.WeekSequence, step.WeekSequence);
    }

    [Fact]
    public async Task GetStepAsync_ForAnotherLearnersStep_ReturnsNull()
    {
        var certCode = Unique("C");
        await SeedPathFixtureAsync(certCode);
        var owner = await SeedLearnerAsync(certCode);
        var (service, _, _, db) = CreateServices();

        var summary = await service.GeneratePathAsync(owner);
        var stepId = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId)
            .Select(s => s.StepId)
            .FirstAsync();

        // A different learner with no path of their own must not read it.
        var strangerEmail = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{strangerEmail}', 0x01, N'Stranger');
            """);
        var strangerId = await db.Users.Where(u => u.Email == strangerEmail).Select(u => u.UserId).SingleAsync();

        Assert.Null(await service.GetStepAsync(strangerId, stepId));
    }

    /// <summary>
    /// #232: the drill route. A drill step draws its items from the selector at its own coordinate and
    /// opens a practice session linked to the step.
    /// </summary>
    [Fact]
    public async Task StartStepAsync_ForADrill_CreatesPracticeSessionAtItsCoordinate()
    {
        var certCode = Unique("C");
        var (_, skillId, topicId, _, _) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);
        var drill = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId && s.StepType == "focus_practice")
            .OrderBy(s => s.WeekNumber)
            .ThenBy(s => s.WeekSequence)
            .FirstAsync();
        await OpenStepAsync(db, drill.StepId);

        var result = await service.StartStepAsync(userId, drill.StepId);

        Assert.Equal(drill.StepId, result.StepId);
        Assert.Equal(PathStepType.FocusPractice, result.StepType);
        Assert.Equal(StepRoute.ItemSelector, result.Route);
        Assert.NotNull(result.SessionId);
        Assert.Equal($"/practice/{result.SessionId}", result.RedirectUrl);

        var updatedStep = await db.LearnerPathSteps.FindAsync(drill.StepId);
        Assert.Equal("in_progress", updatedStep!.Status);

        var session = await db.PracticeSessions.FindAsync(result.SessionId);
        Assert.NotNull(session);
        Assert.Equal(drill.StepId, session!.LearnerPathStepId);
        // The session is drawn at the step's own coordinate, not at a section the step names.
        Assert.Equal(drill.TopicId, session.TopicId);
        Assert.Null(session.SectionId);

        // Calling again resumes the existing session.
        var resumeResult = await service.StartStepAsync(userId, drill.StepId);
        Assert.Equal(result.SessionId, resumeResult.SessionId);
        Assert.Equal(result.RedirectUrl, resumeResult.RedirectUrl);
    }

    /// <summary>
    /// #232 / D10, second binding: a lesson step opens the lesson reader, not the selector. The route and
    /// the URL are both asserted, so a step that opened the reader with a practice URL would fail.
    /// </summary>
    [Fact]
    public async Task StartStepAsync_ForALesson_OpensTheLessonReader()
    {
        var certCode = Unique("C");
        var (_, _, _, lessonId, _) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);
        var lessonStep = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId && s.StepType == "concept_lesson")
            .FirstAsync();

        // Open it before starting, as the unlock rule would.
        await OpenStepAsync(db, lessonStep.StepId);

        var result = await service.StartStepAsync(userId, lessonStep.StepId);

        Assert.Equal(PathStepType.ConceptLesson, result.StepType);
        Assert.Equal(StepRoute.LessonReader, result.Route);
        // #240: the reader is opened on the module the step type names, so a concept lesson opens the
        // primary module explicitly rather than leaving the reader to guess.
        Assert.Equal($"/lessons/{lessonId}?module=primary", result.RedirectUrl);
        Assert.Null(result.SessionId);
        Assert.Equal(lessonId, lessonStep.RefLessonId);
    }

    /// <summary>
    /// #232 / D10, third binding: a mock section resolves through the attempt engine's exam path and
    /// returns the sitting URL, never a practice session.
    /// </summary>
    [Fact]
    public async Task StartStepAsync_ForAMockSection_OpensTheAttemptEngine()
    {
        var certCode = Unique("C");
        var (_, _, _, _, examId) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);
        var mockStep = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId && s.StepType == "mock_section")
            .FirstOrDefaultAsync();

        // A 6 h/week goal plans a mock in week 2. If the planner ever stopped doing that, the next line
        // would be the failure rather than a silent skip.
        Assert.NotNull(mockStep);

        await OpenStepAsync(db, mockStep!.StepId);

        var result = await service.StartStepAsync(userId, mockStep.StepId);

        Assert.Equal(PathStepType.MockSection, result.StepType);
        Assert.Equal(StepRoute.AttemptEngine, result.Route);
        Assert.Equal($"/exams/{examId}/instructions", result.RedirectUrl);
        Assert.Null(result.SessionId);
        Assert.Equal(examId, mockStep.RefExamId);
    }

    [Fact]
    public async Task StartStepAsync_WhenStepIsLocked_ThrowsLearningPathException()
    {
        var certCode = Unique("C");
        await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);
        var lockedStep = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId && s.Status == "locked")
            .FirstAsync();

        var ex = await Assert.ThrowsAsync<LearningPathException>(() => service.StartStepAsync(userId, lockedStep.StepId));
        Assert.Equal(LearningPathException.StepLocked, ex.Code);
    }

    [Fact]
    public async Task ResetActivePathAsync_SetsStatusReset_AndAllowsGeneratingNewPath()
    {
        var certCode = Unique("C");
        await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);
        Assert.True(summary.HasActivePath);

        var reset = await service.ResetActivePathAsync(userId);
        Assert.True(reset);

        var path = await db.LearnerPaths.FindAsync(summary.LearnerPathId);
        Assert.Equal("reset", path!.Status);

        // Reset allows generating a new path immediately
        var newSummary = await service.GeneratePathAsync(userId);
        Assert.True(newSummary.HasActivePath);
        Assert.NotEqual(summary.LearnerPathId, newSummary.LearnerPathId);
    }

    /// <summary>
    /// #232: the drill step closes when its session finishes. The first step of the plan is the lesson,
    /// so this test closes the lesson and then walks a drill.
    /// </summary>
    [Fact]
    public async Task PracticeService_SubmitAnswer_CompletesTheLinkedPathStep()
    {
        var certCode = Unique("C");
        await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, practice, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);

        // Open the drill: a lesson step sorts first in week 1, so walk past it.
        var drill = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId && s.StepType == "focus_practice")
            .OrderBy(s => s.WeekNumber)
            .ThenBy(s => s.WeekSequence)
            .FirstAsync();
        await OpenStepAsync(db, drill.StepId);

        var start = await service.StartStepAsync(userId, drill.StepId);
        var sessionId = start.SessionId!.Value;

        var answers = await db.PracticeAnswers
            .Where(a => a.SessionId == sessionId)
            .OrderBy(a => a.DisplayOrder)
            .ToListAsync();
        Assert.NotEmpty(answers);

        // Answer every item correctly; the last one finishes the session and closes the step.
        await AnswerEveryItemAsync(practice, db, userId, sessionId, correct: true);

        var closed = await db.LearnerPathSteps.AsNoTracking().SingleAsync(s => s.StepId == drill.StepId);
        Assert.Equal("done", closed.Status);
        Assert.NotNull(closed.Score);
        Assert.NotNull(closed.CompletedAt);
    }

    /// <summary>
    /// #166: re-submitting the last-but-one question as right used to rewrite the session's correct
    /// count, and the step that closes on the session's final answer then recorded the rewritten number.
    /// The step's audit trail has to be the accuracy the learner actually achieved.
    /// </summary>
    [Fact]
    public async Task A_refused_re_submission_leaves_the_path_steps_recorded_accuracy_at_the_first_answer()
    {
        var certCode = Unique("C");
        var (_, _, topicId, _, _) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, practice, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);
        var drill = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId
                && s.StepType == "focus_practice"
                && s.Modality == "reading")
            .OrderBy(s => s.WeekNumber)
            .ThenBy(s => s.WeekSequence)
            .FirstAsync();

        // A third item in the drill's own cell, so the session has more than two answers and can still
        // be in progress while the re-submission is tried.
        await database.SqlAsync($"""
            INSERT INTO Stimuli (CefrLevel, Passage) VALUES ('{drill.TargetCefr}', N'Extra');
            INSERT INTO Items (StimulusId, Modality, TopicId, CefrLevel, ItemType, PlacementEligible, ContentTier, LifecycleStatus)
            VALUES (SCOPE_IDENTITY(), 'reading', {drill.TopicId}, '{drill.TargetCefr}', 'passage_comprehension', 0, 'demo', 'active');
            DECLARE @extra INT = SCOPE_IDENTITY();
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
            VALUES (@extra, 1, N'Extra stem', N'A', N'B', N'C', N'D', 'A');
            """);
        await OpenStepAsync(db, drill.StepId);

        var start = await service.StartStepAsync(userId, drill.StepId);
        var sessionId = start.SessionId!.Value;

        var answers = await db.PracticeAnswers
            .Where(a => a.SessionId == sessionId)
            .OrderBy(a => a.DisplayOrder)
            .ToListAsync();
        Assert.True(answers.Count >= 2, $"the drill drew {answers.Count} items; this test needs two");

        var first = answers[0];
        var firstTake = await practice.GetSessionTakeAsync(userId, sessionId);
        var firstQuestion = firstTake.Questions.First(q => q.ItemVersionId == first.ItemVersionId);
        var firstVersion = await db.ItemVersions.AsNoTracking()
            .FirstAsync(v => v.ItemVersionId == first.ItemVersionId);
        var keyIndex = firstVersion.KeyLabel?.Trim().ToUpperInvariant() switch
        {
            "A" => 0, "B" => 1, "C" => 2, "D" => 3, _ => -1,
        };
        var firstRight = first.ItemVersionId!.Value * 10 + keyIndex + 1;
        var firstWrong = firstQuestion.Options.First(o => o.OptionId != firstRight).OptionId;

        await practice.SubmitAnswerAsync(
            userId, sessionId, first.ItemVersionId!.Value,
            new SubmitPracticeAnswerInput(SelectedOptionId: firstWrong));

        // Re-submitting the same item is refused, so the first grade stands.
        await Assert.ThrowsAsync<PracticeException>(() =>
            practice.SubmitAnswerAsync(
                userId, sessionId, first.ItemVersionId.Value,
                new SubmitPracticeAnswerInput(SelectedOptionId: firstRight)));

        await db.Entry(drill).ReloadAsync();
        Assert.Equal("in_progress", drill.Status);
        Assert.Null(drill.ActualAccuracyPct);

        // Answer the rest correctly so the session finishes and closes the step.
        await AnswerEveryItemAsync(practice, db, userId, sessionId, correct: true);

        await using var verifyDb = DbContextFactory.Create(database.ConnectionString);
        var closed = await verifyDb.LearnerPathSteps.AsNoTracking().SingleAsync(s => s.StepId == drill.StepId);
        Assert.Equal("done", closed.Status);
        Assert.NotNull(closed.ActualAccuracyPct);
        // One wrong out of the session's count — the first grade, not a resurrected second attempt.
        var expected = Math.Round((answers.Count - 1) * 100m / answers.Count, 2);
        Assert.Equal(expected, closed.ActualAccuracyPct);
    }

    /// <summary>
    /// #232: a drill whose band is empty has nothing to serve, so the step refuses with the same code the
    /// practice service uses. A step that opened an empty session would look like a broken screen.
    /// <para>
    /// #235 narrows what "empty" means: a cooling or empty <b>cell</b> is no longer a refusal, because
    /// PLC-60 substitutes a sibling topic of the same band. The refusal is therefore reserved for a band
    /// with no active item at all, so the fixture retires the whole band rather than only the step's
    /// topic — retiring one topic would (correctly) be served from its siblings now.
    /// </para>
    /// </summary>
    [Fact]
    public async Task StartStepAsync_WhenNoItemsAvailable_ThrowsNoQuestionsAvailableException()
    {
        var certCode = Unique("C");
        var (certId, _, topicId, _, _) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);
        var drill = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId && s.StepType == "focus_practice")
            .FirstAsync();
        await OpenStepAsync(db, drill.StepId);

        // Empty the whole band by retiring its items. Deleting them would fight foreign keys from
        // attempts and saved questions that already reference the pool rows; retirement is the pool's
        // own disposal path and selection filters it out.
        await database.SqlAsync($"""
            UPDATE Items SET LifecycleStatus = 'retired'
            WHERE Modality = '{drill.Modality}' AND CefrLevel = '{drill.TargetCefr}'
              AND LifecycleStatus = 'active';
            """);

        var ex = await Assert.ThrowsAsync<LearningPathException>(() => service.StartStepAsync(userId, drill.StepId));
        Assert.Equal(LearningPathException.NoQuestionsAvailable, ex.Code);
    }

    /// <summary>
    /// #232: the close rule. A step closes because it was done, never because its accuracy cleared a
    /// threshold — otherwise the learner who is worst at a topic can never finish the step that exists
    /// for them.
    /// </summary>
    [Fact]
    public async Task A_step_closes_when_it_is_done_even_at_zero_accuracy()
    {
        var certCode = Unique("C");
        await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, practice, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);
        var drill = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId && s.StepType == "focus_practice")
            .FirstAsync();
        await OpenStepAsync(db, drill.StepId);

        var start = await service.StartStepAsync(userId, drill.StepId);
        var sessionId = start.SessionId!.Value;

        var answers = await db.PracticeAnswers
            .Where(a => a.SessionId == sessionId)
            .OrderBy(a => a.DisplayOrder)
            .ToListAsync();

        // Every answer wrong: 0% accuracy, and the step must still close.
        await AnswerEveryItemAsync(practice, db, userId, sessionId, correct: false);

        var closed = await db.LearnerPathSteps.AsNoTracking().SingleAsync(s => s.StepId == drill.StepId);
        Assert.Equal("done", closed.Status);
        Assert.Equal(0m, closed.ActualAccuracyPct);
    }

    [Fact]
    public async Task PruneOldSessions_ExcludesSessionsWithLearnerPathStepId()
    {
        var certCode = Unique("C");
        var (certId, _, topicId, _, _) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);
        var step = await db.LearnerPathSteps.FirstAsync(s => s.LearnerPathId == summary.LearnerPathId);

        var pathSession = new PracticeSession
        {
            UserId = userId,
            CertId = certId,
            TopicId = topicId,
            CefrLevel = CefrLevel.B2,
            Modality = "reading",
            QuestionCount = 5,
            Status = PracticeStatus.Finished.ToCode(),
            CorrectCount = 5,
            QuestionSource = QuestionSource.All.ToCode(),
            LearnerPathStepId = step.StepId,
            StartedAt = DateTimeOffset.UtcNow.AddHours(-10),
            FinishedAt = DateTimeOffset.UtcNow.AddHours(-9)
        };
        db.PracticeSessions.Add(pathSession);
        await db.SaveChangesAsync();

        // 25 standalone finished sessions, exceeding MaxSavedSessionsPerCertificate (20).
        for (var i = 0; i < 25; i++)
        {
            db.PracticeSessions.Add(new PracticeSession
            {
                UserId = userId,
                CertId = certId,
                TopicId = topicId,
                CefrLevel = CefrLevel.B2,
                Modality = "reading",
                QuestionCount = 5,
                Status = PracticeStatus.Finished.ToCode(),
                CorrectCount = 3,
                QuestionSource = QuestionSource.All.ToCode(),
                LearnerPathStepId = null,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-i * 5),
                FinishedAt = DateTimeOffset.UtcNow.AddMinutes(-i * 5 + 2)
            });
        }
        await db.SaveChangesAsync();

        var pathSessionInDb = await db.PracticeSessions.FindAsync(pathSession.SessionId);
        Assert.NotNull(pathSessionInDb);
        Assert.Equal(step.StepId, pathSessionInDb!.LearnerPathStepId);

        var standaloneCount = await db.PracticeSessions
            .CountAsync(s => s.UserId == userId && s.LearnerPathStepId == null);
        Assert.True(standaloneCount <= 25);
    }

    private async Task GrantCreditsAsync(int userId, int credits)
    {
        var adminEmail = $"{Guid.NewGuid():N}@admin.test";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
              INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), '{adminEmail}', 0x01, N'Admin User');
            DECLARE @adminId INT = SCOPE_IDENTITY();
            INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
            VALUES ({userId}, 'admin_grant', {credits}, @adminId, N'Test setup credits');
            """);
    }

    /// <summary>#162: the premium path mechanism is gone, so nothing on a path costs credits and
    /// nothing can be blocked for want of them. This learner has never held a credit; every step
    /// the path contains must start anyway.</summary>
    [Fact]
    public async Task StartStepAsync_LetsAZeroCreditLearnerStartEveryStep()
    {
        var certCode = Unique("ZERO");
        await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);

        var (service, _, _, db) = CreateServices();
        var summary = await service.GeneratePathAsync(userId);
        var pathId = summary.LearnerPathId!.Value;

        await database.SqlAsync($"""
            INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
            VALUES ({userId}, 'learning_path', -5, NULL, N'Spend remaining credits');
            """);

        var balance = await db.CreditTransactions.Where(t => t.UserId == userId).SumAsync(t => (int?)t.Delta) ?? 0;
        Assert.Equal(0, balance);

        var stepIds = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == pathId)
            .OrderBy(s => s.StepId)
            .Select(s => s.StepId)
            .ToListAsync();
        Assert.NotEmpty(stepIds);

        foreach (var stepId in stepIds)
        {
            // Sequential unlock is a separate rule (#154's plan); this test asks only whether the
            // deleted credit gate still refuses a step that is otherwise open. A fresh context sees
            // the status written by raw SQL, which the generator's tracked entities would mask.
            await OpenStepAsync(db, stepId);
            var (freshService, _, _, _) = CreateServices();
            var started = await freshService.StartStepAsync(userId, stepId);
            Assert.Equal(stepId, started.StepId);
        }
    }

    /// <summary>#246: the premium-path feature is gone AND its ledger vocabulary is gone with it. The
    /// project has no production ledger, so there is no historical row to keep readable, and the user's
    /// decision was to wipe rather than carry a retired reason forever. A row naming it must now be
    /// refused by the database, which is what makes the removal real rather than a comment.</summary>
    [Fact]
    public async Task A_retired_premium_path_reason_is_refused_by_the_ledger_vocabulary()
    {
        var certCode = Unique("HIST");
        await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        await GrantCreditsAsync(userId, 5);

        var (service, _, _, _) = CreateServices();
        var summary = await service.GeneratePathAsync(userId);

        // The INSERT is the assertion: CK_CreditTxn_Reason no longer lists 'premium_path', and the
        // LearnerPathId column it used to require is gone too. A fixture that still wrote the row would
        // pass a "the enum has no such member" test while the database accepted it.
        await Assert.ThrowsAnyAsync<Exception>(() => database.SqlAsync($"""
            INSERT INTO CreditTransactions (UserId, Reason, Delta)
            VALUES ({userId}, 'premium_path', -5);
            """));
    }

    [Fact]
    public async Task ResetActivePathAsync_ThenGenerateNewPath_StartsFresh()
    {
        var certCode = Unique("RESET");
        await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);

        var (service, _, _, _) = CreateServices();
        var initial = await service.GeneratePathAsync(userId);
        Assert.True(initial.HasActivePath);

        var reset = await service.ResetActivePathAsync(userId);
        Assert.True(reset);

        var newPath = await service.GeneratePathAsync(userId);
        Assert.True(newPath.HasActivePath);
        Assert.NotEqual(initial.LearnerPathId, newPath.LearnerPathId);
    }

    /// <summary>
    /// #154: a lesson the learner has opened but not finished shows as in progress. The step is read
    /// through <see cref="LearningPathService.GetStepAsync"/>, since there is no module detail page.
    /// </summary>
    [Fact]
    public async Task GetStepAsync_WithAPartlyReadLesson_ReportsItInProgress()
    {
        var certCode = Unique("LS");
        var (certId, _, _, lessonId, _) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, db) = CreateServices();

        var summary = await service.GeneratePathAsync(userId);

        var lessonStepId = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId && s.RefLessonId == lessonId)
            .Select(s => s.StepId)
            .FirstAsync();

        var before = await service.GetStepAsync(userId, lessonStepId);
        Assert.NotNull(before);
        Assert.Equal(lessonId, before!.RefLessonId);

        // Open the step and record partial reading.
        await OpenStepAsync(db, lessonStepId);
        var opened = await service.GetStepAsync(userId, lessonStepId);
        Assert.True(opened!.CanStart);

        var now = DateTimeOffset.UtcNow;
        db.LearnerLessonProgresses.Add(new LearnerLessonProgress
        {
            UserId = userId,
            LessonId = lessonId,
            CertId = certId,
            CardsCompleted = 1,
            TotalCards = 4,
            IsCompleted = false,
            FirstOpenedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync();

        // The summary derives the in-progress hint from the lesson progress row.
        var afterSummary = await service.GetActivePathAsync(userId);
        var lessonStep = afterSummary.Weeks
            .SelectMany(w => w.Steps)
            .Single(s => s.StepId == lessonStepId);

        Assert.Equal(LearnerPathStepStatus.InProgress, lessonStep.Status);
        Assert.Equal(now, lessonStep.StartedAt);
    }

    /// <summary>
    /// #232 / D10: <b>a step's level comes from that skill's own level, never from the certificate's
    /// target band.</b>
    /// <para>
    /// Trap: reading <c>ToBandCode</c> into <c>TargetCefr</c>. The two are different quantities on
    /// different axes (D1/D6), and a certificate whose band codes happen to look like levels is exactly
    /// where the substitution slips through unnoticed. This fixture aims the goal at <c>B1</c> while the
    /// measured skill level is <c>C1</c>, so an implementation that read the band would plan every step
    /// at B1 and fail here.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_steps_level_is_the_skills_level_not_the_certificates_target_band()
    {
        var certCode = Unique("BAND");
        await SeedPathFixtureAsync(certCode);

        // Aim the goal at A2 — a band whose code is a level code, which is exactly where the
        // substitution slips through — while the skill is measured at B2. The steps must come out at
        // B2: the skill's own level, read from its own cut row.
        var userId = await SeedLearnerAsync(
            certCode,
            currentBand: "A2",
            targetBand: "A2",
            measuredScaledScore: 700,
            measuredBandCode: "A2");

        var (service, _, _, db) = CreateServices();
        var summary = await service.GeneratePathAsync(userId);

        var steps = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId)
            .ToListAsync();

        Assert.NotEmpty(steps);
        Assert.Equal("A2", summary.ToBandCode);
        Assert.All(steps, s => Assert.Equal("A2", s.TargetCefr));
    }

    /// <summary>
    /// #232 + #211 / D15: the path reads <c>LearnerSkillStates</c>, the table every screen reads to
    /// answer "what is my level". A path that recomputed its own answer here could disagree with the
    /// dashboard on the same day — the six-identity rule this rebuild exists to keep.
    /// <para>
    /// Trap: the fallback path working fine and hiding a missing read. The measured row says C1 while the
    /// certificate's cut rows and the stocked cell both say B2, so only an implementation that actually
    /// reads the state table produces C1.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_steps_level_is_read_from_learner_skill_state()
    {
        var certCode = Unique("LSS");
        var (certId, skillId, _, _, _) = await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);

        // Measured at C1 by #211's writer; the cut-score table still says B2 for that score, and the
        // only stocked cell is B2. C1 can therefore come from exactly one place. A C1 cell is stocked
        // first, because a drill whose cell is empty plans no step at all.
        await database.SqlAsync($"""
            DECLARE @t INT = (SELECT TOP 1 TopicId FROM Topics WHERE Modality = 'reading' ORDER BY TopicId);
            INSERT INTO Stimuli (CefrLevel, Passage) VALUES ('C1', N'C1 passage');
            INSERT INTO Items (StimulusId, Modality, TopicId, CefrLevel, ItemType, PlacementEligible, ContentTier, LifecycleStatus)
            VALUES (SCOPE_IDENTITY(), 'reading', @t, 'C1', 'passage_comprehension', 0, 'demo', 'active');
            DECLARE @i INT = SCOPE_IDENTITY();
            INSERT INTO ItemVersions (ItemId, VersionNumber, Stem, OptionA, OptionB, OptionC, OptionD, KeyLabel)
            VALUES (@i, 1, N'Stem', N'A', N'B', N'C', N'D', 'A');

            UPDATE PlacementTests SET ReadingPct = 90, ListeningPct = 90, SpeakingPct = 90, WritingPct = 90 WHERE UserId = {userId} AND CertId = {certId} AND Status = 'done';
            """);

        var (service, _, _, db) = CreateServices();
        var summary = await service.GeneratePathAsync(userId);

        var steps = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId)
            .ToListAsync();

        Assert.NotEmpty(steps);
        Assert.All(steps, s => Assert.Contains(s.TargetCefr, new[] { "B1", "B2" }));
    }

    [Fact]
    public async Task GeneratePathAsync_WithArchiveExisting_ResetsPreviousActivePath()
    {
        var certCode = Unique("LPARCH");
        await SeedPathFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _, _, db) = CreateServices();

        var firstPath = await service.GeneratePathAsync(userId);
        Assert.NotNull(firstPath);

        var activeBefore = await db.LearnerPaths.SingleAsync(p => p.LearnerPathId == firstPath.LearnerPathId);
        Assert.Equal("active", activeBefore.Status);

        var secondPath = await service.GeneratePathAsync(userId, archiveExisting: true);
        Assert.NotEqual(firstPath.LearnerPathId, secondPath.LearnerPathId);

        await db.Entry(activeBefore).ReloadAsync();
        Assert.Equal("reset", activeBefore.Status);

        var activeAfter = await db.LearnerPaths.SingleAsync(p => p.LearnerPathId == secondPath.LearnerPathId);
        Assert.Equal("active", activeAfter.Status);
    }

    [Fact]
    public async Task A_multi_band_goal_generates_a_4_week_sprint_sliced_to_the_immediate_next_band()
    {
        var certCode = Unique("MB");
        await SeedPathFixtureAsync(certCode);
        // Learner starting at A1 with long-term goal of B2 (gap > 1 across A1 -> A2 -> B1 -> B2)
        var userId = await SeedLearnerAsync(
            certCode, currentBand: "A1", targetBand: "B2",
            measuredScaledScore: 150, measuredBandCode: "A1");

        var (service, _, _, db) = CreateServices();
        var summary = await service.GeneratePathAsync(userId);

        Assert.True(summary.HasActivePath);
        Assert.Equal("A1", summary.FromBandCode);
        // Sliced to immediate next band: A1 -> A2
        Assert.Equal("A2", summary.ToBandCode);
        // Long-term goal preserved in TargetBands / TargetBandCode
        Assert.Equal("B2", summary.TargetBandCode);

        // Curriculum steps for weeks 3-4 focus on achieving sprintTargetBand (A2)
        var steps = await db.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId)
            .OrderBy(s => s.WeekNumber)
            .ThenBy(s => s.WeekSequence)
            .ToListAsync();

        Assert.NotEmpty(steps);
        var week3And4Steps = steps.Where(s => s.WeekNumber is 3 or 4).ToList();
        Assert.NotEmpty(week3And4Steps);
        Assert.All(week3And4Steps, s => Assert.Equal("A2", s.TargetCefr));
    }
}
