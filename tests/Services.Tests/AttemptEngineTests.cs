using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class AttemptEngineTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private AttemptEngine CreateEngine()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        return new AttemptEngine(db);
    }

    private async Task<int> SeedLearnerAsync(string? activeCertCode = null)
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
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    private async Task<(int certId, int examId, int q1Id, int q2Id, int opt1CorrectId, int opt2CorrectId)> SeedExamFixtureAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'LIST', N'Listening', 'listening', 1),
              (@cert, 'READ', N'Reading', 'reading', 2);
            DECLARE @sL INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'LIST');
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder) VALUES
              (@sL, @cert, 'listening', 'P1', N'Photos', 1),
              (@sR, @cert, 'reading', 'P5', N'Sentences', 2);
            DECLARE @secL INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P1');
            DECLARE @secR INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P5');

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'LOW', N'Low', 0, 499, 1),
              (@cert, 'HIGH', N'High', 500, 990, 2);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES
              (@cert, N'Exam {certCode}', 'published', 120, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            -- Exam-specific Scale:
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
              (@cert, @sL, @exam, 0, 100),
              (@cert, @sL, @exam, 1, 450),
              (@cert, @sR, @exam, 0, 100),
              (@cert, @sR, @exam, 1, 450);

            -- Question 1: Listening
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @secL, 1);
            DECLARE @grp1 INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@grp1, @exam, @secL, @sL, N'Listening Q1', 1, 'mcq', 1);
            DECLARE @q1 INT = SCOPE_IDENTITY();
            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@q1, 'mcq', 'A', N'Opt A', 1),
              (@q1, 'mcq', 'B', N'Opt B', 0);

            -- Question 2: Reading
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @secR, 2);
            DECLARE @grp2 INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@grp2, @exam, @secR, @sR, N'Reading Q2', 1, 'mcq', 2);
            DECLARE @q2 INT = SCOPE_IDENTITY();
            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@q2, 'mcq', 'A', N'Opt A', 0),
              (@q2, 'mcq', 'B', N'Opt B', 1);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var questions = await db.Questions.Where(q => q.ExamId == exam.ExamId).OrderBy(q => q.DisplayOrder).ToListAsync();
        var q1 = questions[0];
        var q2 = questions[1];

        var opt1 = await db.QuestionOptions.SingleAsync(o => o.QuestionId == q1.QuestionId && o.IsCorrect);
        var opt2 = await db.QuestionOptions.SingleAsync(o => o.QuestionId == q2.QuestionId && o.IsCorrect);

        return (cert.CertId, exam.ExamId, q1.QuestionId, q2.QuestionId, opt1.OptionId, opt2.OptionId);
    }

    [Fact]
    public async Task CreateAttempt_Seeds_Answers_And_Sets_LastQuestionId()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, q1Id, q2Id, _, _) = await SeedExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        Assert.NotNull(attempt);
        Assert.Equal(AttemptStatus.InProgress.ToCode(), attempt.Status);
        Assert.Equal(q1Id, attempt.LastQuestionId);
        Assert.True(attempt.ExpiresAt > DateTimeOffset.UtcNow);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var answers = await db.AttemptAnswers.Where(a => a.AttemptId == attempt.AttemptId).ToListAsync();
        Assert.Equal(2, answers.Count);
        Assert.All(answers, a =>
        {
            Assert.Null(a.SelectedOptionId);
            Assert.Null(a.IsCorrect);
            Assert.False(a.IsFlagged);
        });
    }

    [Fact]
    public async Task SaveAnswer_Updates_Answer_And_LastQuestionId()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, q1Id, _, opt1CorrectId, _) = await SeedExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        var saveResult = await engine.SaveAnswerAsync(attempt.AttemptId, userId, q1Id, opt1CorrectId, isFlagged: true);

        Assert.Equal(q1Id, saveResult.QuestionId);
        Assert.Equal(opt1CorrectId, saveResult.SelectedOptionId);
        Assert.True(saveResult.IsFlagged);
        Assert.True(saveResult.RemainingSeconds > 0);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var savedAnswer = await db.AttemptAnswers.SingleAsync(a => a.AttemptId == attempt.AttemptId && a.QuestionId == q1Id);
        Assert.Equal(opt1CorrectId, savedAnswer.SelectedOptionId);
        Assert.True(savedAnswer.IsFlagged);

        var savedAttempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
        Assert.Equal(q1Id, savedAttempt.LastQuestionId);
    }

    [Fact]
    public async Task SaveAnswer_Rejects_Invalid_Option()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, q1Id, _, _, _) = await SeedExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        var ex = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveAnswerAsync(attempt.AttemptId, userId, q1Id, 999999, isFlagged: false));
        Assert.Equal(AttemptEngineException.InvalidOption, ex.Code);
    }

    [Fact]
    public async Task SaveAnswer_AutoSubmits_When_Expired_And_Throws_AttemptExpired()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, q1Id, _, opt1CorrectId, _) = await SeedExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        // Expire attempt manually in DB
        await database.SqlAsync($"""
            UPDATE ExamAttempts
            SET ExpiresAt = DATEADD(minute, -10, SYSDATETIMEOFFSET())
            WHERE AttemptId = {attempt.AttemptId}
            """);

        var engine2 = CreateEngine();
        bool callbackInvoked = false;
        var ex = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine2.SaveAnswerAsync(attempt.AttemptId, userId, q1Id, opt1CorrectId, false,
                onAutoSubmitted: (attId, uId) =>
                {
                    callbackInvoked = true;
                    return Task.CompletedTask;
                }));

        Assert.Equal(AttemptEngineException.AttemptExpired, ex.Code);
        Assert.True(callbackInvoked);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var reloaded = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
        Assert.Equal(AttemptStatus.Graded.ToCode(), reloaded.Status);
        Assert.True(reloaded.IsAutoSubmitted);
    }

    [Fact]
    public async Task SubmitAttempt_Scores_Answers_Computes_Scales_And_Matches_ScoreBand()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, q1Id, q2Id, opt1CorrectId, opt2CorrectId) = await SeedExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        await engine.SaveAnswerAsync(attempt.AttemptId, userId, q1Id, opt1CorrectId, false);
        await engine.SaveAnswerAsync(attempt.AttemptId, userId, q2Id, opt2CorrectId, false);

        var result = await engine.SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: false);

        // LIST 1 correct -> 450, READ 1 correct -> 450, Total = 900 -> Band 'HIGH' (500-990)
        Assert.Equal(900, result.TotalScore);
        Assert.Equal("HIGH", result.BandCode);
        Assert.Equal(AttemptStatus.Graded.ToCode(), result.Attempt.Status);
        Assert.Equal(2, result.SkillScores.Count);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var attemptInDb = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
        Assert.Equal(900, attemptInDb.TotalScore);
        Assert.Equal("HIGH", attemptInDb.BandCode);
        Assert.Equal(AttemptStatus.Graded.ToCode(), attemptInDb.Status);
    }

    [Fact]
    public async Task SubmitAttempt_Is_Idempotent()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, q1Id, q2Id, opt1CorrectId, opt2CorrectId) = await SeedExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);
        await engine.SaveAnswerAsync(attempt.AttemptId, userId, q1Id, opt1CorrectId, false);
        await engine.SaveAnswerAsync(attempt.AttemptId, userId, q2Id, opt2CorrectId, false);

        var first = await engine.SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: false);
        var second = await engine.SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: false);

        Assert.Equal(first.TotalScore, second.TotalScore);
        Assert.Equal(first.BandCode, second.BandCode);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var skillScoreCount = await db.AttemptSkillScores.CountAsync(s => s.AttemptId == attempt.AttemptId);
        Assert.Equal(2, skillScoreCount);
    }

    [Fact]
    public async Task SubmitAttempt_AutoSyncs_Linked_PlacementTest()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, q1Id, q2Id, opt1CorrectId, opt2CorrectId) = await SeedExamFixtureAsync(certCode);

        // Set Exam purpose to placement to satisfy FK_Placement_ExamPurpose
        await database.SqlAsync($"""
            UPDATE Exams SET Purpose = 'placement' WHERE ExamId = {examId};
            """);

        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            db.PlacementTests.Add(new PlacementTest
            {
                UserId = userId,
                CertId = certId,
                ExamId = examId,
                AttemptId = attempt.AttemptId,
                Status = PlacementStatus.InProgress.ToCode(),
                ExamPurpose = ExamPurpose.Placement.ToCode(),
                TakenAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var engine2 = CreateEngine();
        await engine2.SaveAnswerAsync(attempt.AttemptId, userId, q1Id, opt1CorrectId, false);
        await engine2.SaveAnswerAsync(attempt.AttemptId, userId, q2Id, opt2CorrectId, false);

        var grading = await engine2.SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: false);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var placement = await db.PlacementTests.SingleAsync(p => p.AttemptId == attempt.AttemptId);
            Assert.Equal(PlacementStatus.Done.ToCode(), placement.Status);
            Assert.Equal(grading.TotalScore, placement.TotalScore);
            Assert.Equal(grading.BandCode, placement.ResultBandCode);
        }
    }

    [Fact]
    public async Task ComputeScaledScore_Falls_Back_Through_Hierarchy()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, _, _, _, _) = await SeedExamFixtureAsync(certCode);
        var engine = CreateEngine();

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var skill = await db.Skills.FirstAsync(s => s.CertId == certId);

        // 1. Exam-specific scale exists for raw score 1 -> returns 450
        var score1 = await engine.ComputeScaledScoreAsync(certId, skill.SkillId, examId, 1, 1);
        Assert.Equal(450, score1);

        // 2. Default scale (ExamId == null): seed a default cert scale for another raw score
        await database.SqlAsync($"""
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore)
            VALUES ({certId}, {skill.SkillId}, NULL, 5, 300);
            """);

        var scoreDefault = await engine.ComputeScaledScoreAsync(certId, skill.SkillId, 99999, 5, 10);
        Assert.Equal(300, scoreDefault);

        // 3. Proportional fallback when no scale matches: 0/10 -> 5
        var scoreFallback = await engine.ComputeScaledScoreAsync(99999, 99999, 99999, 0, 10);
        Assert.Equal(5, scoreFallback);
    }

    [Fact]
    public async Task AutoSubmitExpiredAttempts_Processes_All_Expired_Attempts()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, _, _, _, _) = await SeedExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        // Expire attempt in DB
        await database.SqlAsync($"""
            UPDATE ExamAttempts
            SET ExpiresAt = DATEADD(minute, -30, SYSDATETIMEOFFSET())
            WHERE AttemptId = {attempt.AttemptId}
            """);

        var engine2 = CreateEngine();
        int processed = await engine2.AutoSubmitExpiredAttemptsAsync(userId, certId);
        Assert.Equal(1, processed);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var reloaded = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
        Assert.Equal(AttemptStatus.Graded.ToCode(), reloaded.Status);
        Assert.True(reloaded.IsAutoSubmitted);
    }

    [Fact]
    public async Task CancelAttempt_Marks_Attempt_Abandoned()
    {
        var certCode = Unique("ATTE");
        var (certId, examId, _, _, _, _) = await SeedExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);

        await engine.CancelAttemptAsync(attempt.AttemptId, userId);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var reloaded = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
        Assert.Equal(AttemptStatus.Abandoned.ToCode(), reloaded.Status);
    }
}
