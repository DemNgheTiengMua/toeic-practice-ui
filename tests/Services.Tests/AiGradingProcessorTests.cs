using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class AiGradingProcessorTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private async Task<int> SeedLearnerAsync(string? activeCertCode = null, int startingCredits = 0)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Test Learner',
                    (SELECT CertId FROM Certificates WHERE Code = {(activeCertCode is null ? "NULL" : $"'{activeCertCode}'")}));
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();

        if (startingCredits > 0)
        {
            await database.SqlAsync($"""
                IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
                  INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
                INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
                VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), 'admin-{Guid.NewGuid():N}@test.local', 0x01, N'Admin', SYSDATETIMEOFFSET());
                DECLARE @adminId INT = SCOPE_IDENTITY();
                INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
                VALUES ({userId}, 'admin_grant', {startingCredits}, @adminId, N'Test setup credits');
                """);
        }

        return userId;
    }

    private async Task<(int certId, int examId, int q1Id, int q2Id)> SeedWritingExamFixtureAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes) VALUES
              (@cert, 'WRIT', N'Writing', 'writing', 1, 60);
            DECLARE @sW INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'WRIT');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder) VALUES
              (@sW, @cert, 'writing', 'W1', N'Sentence from picture', 1, 1),
              (@sW, @cert, 'writing', 'W2', N'Essay', 1, 2);
            DECLARE @sec1 INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'W1');
            DECLARE @sec2 INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'W2');

            -- Criteria
            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder) VALUES
              (@cert, @sW, 'GRAM', N'Grammar', 5.0, 1.0, 1),
              (@cert, @sW, 'VOCAB', N'Vocabulary', 5.0, 1.0, 2);
            DECLARE @crit1 INT = (SELECT CriterionId FROM GradingCriteria WHERE CertId = @cert AND Code = 'GRAM');
            DECLARE @crit2 INT = (SELECT CriterionId FROM GradingCriteria WHERE CertId = @cert AND Code = 'VOCAB');

            -- Section override for W1: MaxScore 3.0
            INSERT INTO SectionCriteria (SectionId, CriterionId, SkillId, MaxScore) VALUES
              (@sec1, @crit1, @sW, 3.0),
              (@sec1, @crit2, @sW, 3.0);

            -- Exam
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, QuestionCount) VALUES
              (@cert, N'Writing Practice {certCode}', 'published', 60, 'writing_practice', 2);
            DECLARE @exam INT = SCOPE_IDENTITY();

            -- Question 1: Sentence from picture
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec1, 1);
            DECLARE @grp1 INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, MinWords, MaxWords, DisplayOrder)
            VALUES (@grp1, @exam, @sec1, @sW, N'Write a sentence describing the picture.', 1, 'free_response', 5, 25, 1);
            DECLARE @q1 INT = SCOPE_IDENTITY();

            -- Question 2: Essay
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec2, 2);
            DECLARE @grp2 INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, MinWords, MaxWords, DisplayOrder)
            VALUES (@grp2, @exam, @sec2, @sW, N'Write an essay giving your opinion on remote work.', 3, 'free_response', 30, 300, 2);
            DECLARE @q2 INT = SCOPE_IDENTITY();
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var questions = await db.Questions.Where(q => q.ExamId == exam.ExamId).OrderBy(q => q.DisplayOrder).ToListAsync();

        return (cert.CertId, exam.ExamId, questions[0].QuestionId, questions[1].QuestionId);
    }

    [Fact]
    public async Task ProcessPendingGradingsAsync_clamps_scores_to_criterion_max_and_updates_scores()
    {
        var certCode = Unique("GP01");
        var (_, examId, q1Id, q2Id) = await SeedWritingExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 3);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var learners = new LearnerService(db);
        var writingService = new WritingPracticeService(db, learners);

        var info = await writingService.StartAttemptAsync(userId, examId);
        await writingService.SaveDraftAsync(userId, info.AttemptId, q1Id, "A woman is reading a book.");
        await writingService.SaveDraftAsync(userId, info.AttemptId, q2Id, "Working from home provides flexibility.");
        await writingService.SubmitAttemptAsync(userId, info.AttemptId);

        // Create a custom grader that returns overly high scores (e.g. 10.0 on a 3.0 max criterion)
        var customGrader = new CustomScoreGrader(10.0m);
        var processor = new AiGradingProcessor(db, customGrader);

        var processed = await processor.ProcessPendingGradingsAsync(10);
        Assert.True(processed >= 2);

        await using var dbCheck = DbContextFactory.Create(database.ConnectionString);
        var scoresQ1 = await dbCheck.AiGradingScores
            .Include(s => s.AiGrading)
            .Where(s => s.AiGrading.FreeResponse.AttemptId == info.AttemptId && s.AiGrading.FreeResponse.QuestionId == q1Id)
            .ToListAsync();

        // Q1 section override MaxScore is 3.0, so 10.0 must be clamped to 3.0
        Assert.Equal(2, scoresQ1.Count);
        Assert.All(scoresQ1, s => Assert.Equal(3.0m, s.Score));

        // Attempt should be marked graded
        var attempt = await dbCheck.ExamAttempts.SingleAsync(a => a.AttemptId == info.AttemptId);
        Assert.Equal("graded", attempt.Status);
        Assert.Null(attempt.TotalScore);
        Assert.Null(attempt.BandCode);
    }

    [Fact]
    public async Task GradeFreeResponseAsync_grades_targeted_single_response_by_id()
    {
        var certCode = Unique("GP02");
        var (_, examId, q1Id, q2Id) = await SeedWritingExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 3);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var learners = new LearnerService(db);
        var writingService = new WritingPracticeService(db, learners);

        var info = await writingService.StartAttemptAsync(userId, examId);
        await writingService.SaveDraftAsync(userId, info.AttemptId, q1Id, "Answer for question 1.");
        await writingService.SaveDraftAsync(userId, info.AttemptId, q2Id, "Answer for question 2.");
        await writingService.SubmitAttemptAsync(userId, info.AttemptId);

        var freeResponses = await db.FreeResponses.Where(fr => fr.AttemptId == info.AttemptId).ToListAsync();
        var fr1 = freeResponses.Single(r => r.QuestionId == q1Id);
        var fr2 = freeResponses.Single(r => r.QuestionId == q2Id);

        var fakeGrader = new FakeAiGrader();
        var processor = new AiGradingProcessor(db, fakeGrader);

        // Grade ONLY fr1
        var graded = await processor.GradeFreeResponseAsync(fr1.FreeResponseId);
        Assert.True(graded);

        await using var dbCheck = DbContextFactory.Create(database.ConnectionString);
        var g1 = await dbCheck.AiGradings.SingleAsync(g => g.FreeResponseId == fr1.FreeResponseId);
        var g2 = await dbCheck.AiGradings.SingleAsync(g => g.FreeResponseId == fr2.FreeResponseId);

        Assert.Equal("done", g1.Status);
        Assert.Equal("pending", g2.Status);

        // Attempt is NOT yet graded because fr2 is still pending
        var attempt = await dbCheck.ExamAttempts.SingleAsync(a => a.AttemptId == info.AttemptId);
        Assert.Equal("submitted", attempt.Status);

        // Now grade fr2
        var graded2 = await processor.GradeFreeResponseAsync(fr2.FreeResponseId);
        Assert.True(graded2);

        await using var dbCheck2 = DbContextFactory.Create(database.ConnectionString);
        var attemptFinished = await dbCheck2.ExamAttempts.SingleAsync(a => a.AttemptId == info.AttemptId);
        Assert.Equal("graded", attemptFinished.Status);
    }

    [Fact]
    public async Task ProcessPendingGradingsAsync_marks_failed_on_grader_error_and_preserves_queue_progression()
    {
        var certCode = Unique("GP03");
        var (_, examId, q1Id, q2Id) = await SeedWritingExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 3);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var learners = new LearnerService(db);
        var writingService = new WritingPracticeService(db, learners);

        var info = await writingService.StartAttemptAsync(userId, examId);
        // Put simulate_fail marker in q1
        await writingService.SaveDraftAsync(userId, info.AttemptId, q1Id, "Answer with [simulate_fail] marker.");
        await writingService.SaveDraftAsync(userId, info.AttemptId, q2Id, "Valid normal answer.");
        await writingService.SubmitAttemptAsync(userId, info.AttemptId);

        var processor = new AiGradingProcessor(db, new FakeAiGrader());
        var processed = await processor.ProcessPendingGradingsAsync(10);
        Assert.True(processed >= 2);

        await using var dbCheck = DbContextFactory.Create(database.ConnectionString);
        var g1 = await dbCheck.AiGradings.Include(g => g.FreeResponse).SingleAsync(g => g.FreeResponse.AttemptId == info.AttemptId && g.FreeResponse.QuestionId == q1Id);
        var g2 = await dbCheck.AiGradings.Include(g => g.FreeResponse).SingleAsync(g => g.FreeResponse.AttemptId == info.AttemptId && g.FreeResponse.QuestionId == q2Id);

        Assert.Equal("failed", g1.Status);
        Assert.NotNull(g1.FailureReason);
        Assert.Equal("done", g2.Status);
    }

    private sealed class CustomScoreGrader(decimal fixedScore) : IAiGrader
    {
        public Task<AiGradingResult> GradeAsync(AiGradingRequest request, CancellationToken cancellationToken = default)
        {
            var scores = request.Criteria.Select(c => new AiCriterionScoreResult(c.CriterionId, fixedScore, "Overridden score")).ToList();
            return Task.FromResult(new AiGradingResult(
                Success: true,
                FailureReason: null,
                Model: "custom-grader",
                Scores: scores));
        }
    }
}
