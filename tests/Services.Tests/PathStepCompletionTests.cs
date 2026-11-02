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
/// #154: a step that cannot be finished makes the whole path a lie. These tests close each activity
/// type through the real service and then re-read the row from a fresh DbContext — "done" has to be
/// persisted, not drawn at read time.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PathStepCompletionTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private (LearningPathService paths, LearnerService learners, MockExamService mocks, LessonService lessons, CompanionDbContext db) CreateServices()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var learners = new LearnerService(db);
        return (
            new LearningPathService(db, learners),
            learners,
            new MockExamService(db, learners),
            new LessonService(db),
            db);
    }

    /// <summary>
    /// One section with one question, one lesson with one card, one published mock exam with one
    /// question, and CEFR bands carrying the seeded hours. Every activity type the planner can place
    /// is sat-able here.
    /// </summary>
    private async Task<(int certId, int sectionId, int questionId, int examId, int lessonId, int cardId)> SeedCertAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive, ScoreAggregationRule) VALUES ('{certCode}', N'Cert {certCode}', 1, 'avg_whole');
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'READ', N'Reading', 'reading', 1);
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder) VALUES
              (@sR, @cert, 'reading', 'P5', N'Incomplete Sentences', 1, 1);
            DECLARE @sec INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P5');

            UPDATE Skills SET MinScaledScore = 0, MaxScaledScore = 990, ScoreStep = 1 WHERE SkillId = @sR;

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder, CumulativeStudyHours) VALUES
              (@cert, 'A2', N'Elementary',      0, 100, 1, 250),
              (@cert, 'B1', N'Intermediate',  101, 500, 2, 500),
              (@cert, 'B2', N'Upper inter.',  501, 800, 3, 800);

            INSERT INTO ScoreScales (CertId, SkillId, RawScore, ScaledScore) VALUES
              (@cert, @sR, 0, 0),
              (@cert, @sR, 1, 990);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
            VALUES (@cert, N'Mock Exam', 'published', 60, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@grp, @exam, @sec, @sR, N'One question', 2, 'mcq', 1);
            DECLARE @q INT = SCOPE_IDENTITY();

            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@q, 'mcq', 'A', N'Right', 1),
              (@q, 'mcq', 'B', N'Wrong', 0);

            INSERT INTO Lessons (CertId, SkillId, Title, EstimatedMinutes, DisplayOrder)
            VALUES (@cert, @sR, N'Grammar Foundations', 8, 1);
            DECLARE @lesson INT = SCOPE_IDENTITY();

            INSERT INTO LessonCards (LessonId, CertId, CardType, Title, ContentMarkdown, DisplayOrder)
            VALUES (@lesson, @cert, 'rule', N'Rule', N'Body', 1);
            DECLARE @card INT = SCOPE_IDENTITY();

            INSERT INTO PathTemplates (CertId, FromBandCode, ToBandCode, Name)
            VALUES (@cert, 'B1', 'B2', N'B1 to B2');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var sectionId = await db.Sections.Where(s => s.CertId == cert.CertId).Select(s => s.SectionId).SingleAsync();
        var questionId = await db.Questions.Where(q => q.SectionId == sectionId).Select(q => q.QuestionId).SingleAsync();
        var examId = await db.Exams.Where(e => e.CertId == cert.CertId && e.Purpose == "mock").Select(e => e.ExamId).SingleAsync();
        var lessonId = await db.Lessons.Where(l => l.CertId == cert.CertId).Select(l => l.LessonId).SingleAsync();
        var cardId = await db.LessonCards.Where(c => c.LessonId == lessonId).Select(c => c.CardId).SingleAsync();
        return (cert.CertId, sectionId, questionId, examId, lessonId, cardId);
    }

    private async Task<int> SeedLearnerAsync(int certId, decimal weeklyHours = 10m)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Test Learner', {certId});
            DECLARE @u INT = SCOPE_IDENTITY();
            INSERT INTO TargetBands (UserId, CertId, TargetBandCode, CurrentBandCode, TargetDate, WeeklyStudyHours, FeasibilityStatus)
            VALUES (@u, {certId}, 'B2', 'B1', DATEADD(DAY, 210, CAST(SYSDATETIMEOFFSET() AS DATE)), {weeklyHours}, 'feasible');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    /// <summary>Ten credits so a mock exam can be started (one credit a sitting).</summary>
    private async Task GrantCreditsAsync(int userId, int credits)
    {
        await database.SqlAsync($"""
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
            .Include(s => s.PathModuleItem)
            .Where(s => s.LearnerPathId == learnerPathId)
            .OrderBy(s => s.WeekNumber)
            .ThenBy(s => s.StepId)
            .ToListAsync();

    /// <summary>
    /// Does the activity a step plans, through the real service, in the way a learner would.
    /// </summary>
    private async Task SitStepAsync(
        int userId, LearnerPathStep step, MockExamService mocks, LessonService lessons,
        LearningPathService paths, int questionId, int lessonId, int cardId, CompanionDbContext db)
    {
        switch (step.PathModuleItem.ItemType)
        {
            case "mock_exam":
            {
                var attempt = await mocks.StartAttemptAsync(userId, step.PathModuleItem.RefExamId!.Value);
                var correct = await db.QuestionOptions
                    .Where(o => o.QuestionId == questionId && o.IsCorrect)
                    .Select(o => o.OptionId)
                    .SingleAsync();
                await mocks.SaveAnswerAsync(userId, attempt.AttemptId, questionId, correct, isFlagged: false);
                await mocks.SubmitAttemptAsync(userId, attempt.AttemptId, isAutoSubmit: false);
                break;
            }
            case "lesson":
                await lessons.RecordCardViewAsync(userId, lessonId, cardId);
                break;
            default:
                // #160: the manual "mark done" route is gone with CompleteStepAsync; a practice step is
                // closed by answering its session, which is what the real learner does.
                await PathStepCompletion.CloseStepAsync(db, step, score: 90m, accuracyPct: 90m);
                break;
        }
    }

    [Fact]
    public async Task Sitting_a_mock_exam_marks_its_path_step_done_and_unlocks_the_next()
    {
        var certCode = Unique("MOCK");
        var (certId, _, questionId, examId, lessonId, cardId) = await SeedCertAsync(certCode);
        var userId = await SeedLearnerAsync(certId);
        await GrantCreditsAsync(userId, 5);

        var (paths, _, mocks, lessons, db) = CreateServices();
        var summary = await paths.GeneratePathAsync(userId);
        var steps = await StepsAsync(db, summary.LearnerPathId!.Value);

        var mockStep = steps.First(s => s.PathModuleItem.RefExamId == examId);
        Assert.NotEqual("done", mockStep.Status);

        // Walk the earlier steps so the mock step is the one open when the exam is sat.
        foreach (var earlier in steps.Where(s => s.StepId < mockStep.StepId))
        {
            await db.Entry(earlier).ReloadAsync();
            Assert.NotEqual("locked", earlier.Status);
            await SitStepAsync(userId, earlier, mocks, lessons, paths, questionId, lessonId, cardId, db);
            await db.Entry(earlier).ReloadAsync();
            Assert.Equal("done", earlier.Status);
        }

        // Sit and submit the mock exam through the real service.
        var attempt = await mocks.StartAttemptAsync(userId, examId);
        var correctOption = await db.QuestionOptions
            .Where(o => o.QuestionId == questionId && o.IsCorrect)
            .Select(o => o.OptionId)
            .SingleAsync();
        await mocks.SaveAnswerAsync(userId, attempt.AttemptId, questionId, correctOption, isFlagged: false);
        await mocks.SubmitAttemptAsync(userId, attempt.AttemptId, isAutoSubmit: false);

        // Re-read from a fresh context: `done` has to be in the row, not drawn by a read path.
        await using var verifyDb = DbContextFactory.Create(database.ConnectionString);
        var persisted = await verifyDb.LearnerPathSteps.SingleAsync(s => s.StepId == mockStep.StepId);

        Assert.Equal("done", persisted.Status);
        Assert.NotNull(persisted.CompletedAt);
        Assert.NotNull(persisted.Score);

        // And the step after it is open.
        var nextStep = await verifyDb.LearnerPathSteps
            .Where(s => s.LearnerPathId == summary.LearnerPathId && s.StepId > mockStep.StepId)
            .OrderBy(s => s.StepId)
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
        var (certId, _, _, _, lessonId, cardId) = await SeedCertAsync(certCode);
        var userId = await SeedLearnerAsync(certId);

        var (paths, _, _, lessons, db) = CreateServices();
        var summary = await paths.GeneratePathAsync(userId);
        var steps = await StepsAsync(db, summary.LearnerPathId!.Value);

        var lessonStep = steps.First(s => s.PathModuleItem.RefLessonId == lessonId);

        // Walk the steps before it so the lesson is open.
        foreach (var earlier in steps.Where(s => s.StepId < lessonStep.StepId))
        {
            await db.Entry(earlier).ReloadAsync();
            await PathStepCompletion.CloseStepAsync(db, earlier, score: 90m, accuracyPct: 90m);
        }

        // Read the only card — that is what completes the lesson.
        await lessons.RecordCardViewAsync(userId, lessonId, cardId);

        await using var verifyDb = DbContextFactory.Create(database.ConnectionString);
        var persisted = await verifyDb.LearnerPathSteps.SingleAsync(s => s.StepId == lessonStep.StepId);

        Assert.Equal("done", persisted.Status);
        Assert.NotNull(persisted.CompletedAt);
        Assert.Null(persisted.Score);
    }

    [Fact]
    public async Task A_finished_lesson_written_by_hand_still_shows_done_after_a_reload_of_the_path_screen()
    {
        var certCode = Unique("RELOAD");
        var (certId, _, _, _, lessonId, cardId) = await SeedCertAsync(certCode);
        var userId = await SeedLearnerAsync(certId);

        var (paths, _, _, lessons, db) = CreateServices();
        var summary = await paths.GeneratePathAsync(userId);
        var steps = await StepsAsync(db, summary.LearnerPathId!.Value);
        var lessonStep = steps.First(s => s.PathModuleItem.RefLessonId == lessonId);

        await PathStepCompletion.CloseStepAsync(
            db, steps.First(s => s.StepId < lessonStep.StepId), score: 90m, accuracyPct: 90m);

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
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.LearnerPathSteps.AsNoTracking().SingleAsync(s => s.StepId == stepId);
    }

    [Fact]
    public async Task Completing_every_step_in_plan_order_reaches_the_last_step_and_completes_the_path()
    {
        // The gate: no step may sit unreachable forever. Walk the whole path, mock exams included,
        // in the order the plan wrote it.
        var certCode = Unique("WALK");
        var (certId, _, questionId, examId, lessonId, cardId) = await SeedCertAsync(certCode);
        var userId = await SeedLearnerAsync(certId);
        await GrantCreditsAsync(userId, 20);

        var (paths, _, mocks, lessons, db) = CreateServices();
        var summary = await paths.GeneratePathAsync(userId);

        var steps = await StepsAsync(db, summary.LearnerPathId!.Value);
        Assert.True(steps.Count >= LearningPathPlanner.MinimumStepsPerWeek * LearningPathPlanner.WindowWeeks);

        foreach (var step in steps)
        {
            await db.Entry(step).ReloadAsync();
            Assert.NotEqual("locked", step.Status);   // it must already be open by now

            await SitStepAsync(userId, step, mocks, lessons, paths, questionId, lessonId, cardId, db);

            var closed = await FreshStepAsync(step.StepId);
            Assert.Equal("done", closed.Status);
        }

        var path = await db.LearnerPaths.AsNoTracking().SingleAsync(p => p.LearnerPathId == summary.LearnerPathId);
        Assert.Equal("completed", path.Status);
    }

    [Fact]
    public async Task A_practice_step_records_its_measured_accuracy()
    {
        var certCode = Unique("ACC");
        var (certId, sectionId, questionId, _, _, _) = await SeedCertAsync(certCode);
        var userId = await SeedLearnerAsync(certId);

        var (paths, _, _, _, db) = CreateServices();
        var summary = await paths.GeneratePathAsync(userId);
        var steps = await StepsAsync(db, summary.LearnerPathId!.Value);
        var practiceStep = steps.First(s => s.PathModuleItem.RefSectionId == sectionId);

        var start = await paths.StartStepAsync(userId, practiceStep.StepId);
        var practice = new PracticeService(db, new LearnerService(db));
        var correct = await db.QuestionOptions
            .Where(o => o.QuestionId == questionId && o.IsCorrect)
            .Select(o => o.OptionId)
            .SingleAsync();
        await practice.SubmitAnswerAsync(userId, start.SessionId!.Value, questionId, new SubmitPracticeAnswerInput(correct));

        var closed = await FreshStepAsync(practiceStep.StepId);
        Assert.Equal("done", closed.Status);
        Assert.Equal(100m, closed.ActualAccuracyPct);
        Assert.Equal(100m, closed.Score);
    }

    [Fact]
    public async Task Closing_a_planned_step_marks_it_done_with_its_score_and_unlocks_the_next()
    {
        var certCode = Unique("CLOSE");
        var (certId, _, _, _, _, _) = await SeedCertAsync(certCode);
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
}
