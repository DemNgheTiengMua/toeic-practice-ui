using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class PreflightGateTests(DatabaseFixture database)
{
    private AttemptEngine CreateEngine()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        return new AttemptEngine(db);
    }

    private static string Unique(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..12];

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

    private async Task<(int certId, int examId, int q1Id, int q2Id)> SeedExamWithRequireAudioAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'LIST', N'Listening', 'listening', 1),
              (@cert, 'READ', N'Reading', 'reading', 2);
            DECLARE @sL INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'LIST');
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, RequireAudio) VALUES
              (@sL, @cert, 'listening', 'P1', N'Photos', 1, 1),
              (@sR, @cert, 'reading', 'P5', N'Sentences', 2, 0);
            DECLARE @secL INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P1');
            DECLARE @secR INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P5');

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'LOW', N'Low', 0, 499, 1),
              (@cert, 'HIGH', N'High', 500, 990, 2);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES
              (@cert, N'Exam {certCode}', 'published', 120, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @secL, 1);
            DECLARE @gL INT = SCOPE_IDENTITY();
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @secR, 2);
            DECLARE @gR INT = SCOPE_IDENTITY();

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@gL, @exam, @secL, @sL, N'Listening question', 1, 'mcq', 1);
            DECLARE @q1 INT = SCOPE_IDENTITY();

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@gR, @exam, @secR, @sR, N'Reading question', 1, 'mcq', 2);
            DECLARE @q2 INT = SCOPE_IDENTITY();

            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@q1, 'mcq', 'A', 'Opt 1A', 1), (@q1, 'mcq', 'B', 'Opt 1B', 0),
              (@q2, 'mcq', 'A', 'Opt 2A', 1), (@q2, 'mcq', 'B', 'Opt 2B', 0);

            INSERT INTO ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes, UploadedAt)
            VALUES (@exam, @secL, @cert, '/media/audio/track1.mp3', 120, 1024000, SYSDATETIMEOFFSET());
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var exam = await db.Exams.SingleAsync(e => e.Cert.Code == certCode);
        var questions = await db.Questions.Where(q => q.ExamId == exam.ExamId).OrderBy(q => q.DisplayOrder).ToListAsync();
        return (exam.CertId, exam.ExamId, questions[0].QuestionId, questions[1].QuestionId);
    }

    private async Task<(int certId, int examId, int qSpeakingId)> SeedExamWithSpeakingSkillAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'speaking', N'Speaking', 'speaking', 1);
            DECLARE @sS INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'speaking');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, RequireAudio) VALUES
              (@sS, @cert, 'speaking', 'S1', N'Speaking Part 1', 1, 0);
            DECLARE @secS INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'S1');

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'LOW', N'Low', 0, 499, 1),
              (@cert, 'HIGH', N'High', 500, 990, 2);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES
              (@cert, N'Speaking Exam {certCode}', 'published', 45, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @secS, 1);
            DECLARE @gS INT = SCOPE_IDENTITY();

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@gS, @exam, @secS, @sS, N'Describe your hometown.', 1, 'free_response', 1);
            DECLARE @q1 INT = SCOPE_IDENTITY();
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var exam = await db.Exams.SingleAsync(e => e.Cert.Code == certCode);
        var q = await db.Questions.SingleAsync(q => q.ExamId == exam.ExamId);
        return (exam.CertId, exam.ExamId, q.QuestionId);
    }

    private async Task<(int certId, int examId, int qReadingId)> SeedPureReadingExamAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'READ', N'Reading', 'reading', 1);
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, RequireAudio) VALUES
              (@sR, @cert, 'reading', 'P5', N'Sentences', 1, 0);
            DECLARE @secR INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P5');

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'LOW', N'Low', 0, 499, 1),
              (@cert, 'HIGH', N'High', 500, 990, 2);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES
              (@cert, N'Reading Exam {certCode}', 'published', 60, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @secR, 1);
            DECLARE @gR INT = SCOPE_IDENTITY();

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@gR, @exam, @secR, @sR, N'Reading sentence', 1, 'mcq', 1);
            DECLARE @q1 INT = SCOPE_IDENTITY();

            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@q1, 'mcq', 'A', 'Opt A', 1), (@q1, 'mcq', 'B', 'Opt B', 0);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var exam = await db.Exams.SingleAsync(e => e.Cert.Code == certCode);
        var q = await db.Questions.SingleAsync(q => q.ExamId == exam.ExamId);
        return (exam.CertId, exam.ExamId, q.QuestionId);
    }

    [Fact]
    public async Task Exam_With_RequireAudio_RequiresPreflight_And_PreflightVerifiedAt_Starts_Null()
    {
        var certCode = Unique("PFAU");
        var (certId, examId, _, _) = await SeedExamWithRequireAudioAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var requiresPreflight = await engine.RequiresPreflightAsync(examId);
        Assert.True(requiresPreflight);

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 120);
        Assert.NotNull(attempt);
        Assert.Null(attempt.PreflightVerifiedAt);

        var status = await engine.GetPreflightStatusAsync(attempt.AttemptId, userId);
        Assert.True(status.RequiresPreflight);
        Assert.True(status.RequiresAudio);
        Assert.False(status.RequiresMicrophone);
        Assert.False(status.IsVerified);
        Assert.Single(status.AudioUrls);
        Assert.Equal("/media/audio/track1.mp3", status.AudioUrls[0]);
    }

    [Fact]
    public async Task Exam_With_Speaking_Skill_RequiresPreflight_And_RequiresMicrophone()
    {
        var certCode = Unique("PFSP");
        var (certId, examId, _) = await SeedExamWithSpeakingSkillAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var requiresPreflight = await engine.RequiresPreflightAsync(examId);
        Assert.True(requiresPreflight);

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 45);
        Assert.Null(attempt.PreflightVerifiedAt);

        var status = await engine.GetPreflightStatusAsync(attempt.AttemptId, userId);
        Assert.True(status.RequiresPreflight);
        Assert.True(status.RequiresMicrophone);
        Assert.False(status.IsVerified);
    }

    [Fact]
    public async Task Pure_Reading_Exam_Does_Not_Require_Preflight_And_Is_Immediately_Verified()
    {
        var certCode = Unique("PFRD");
        var (certId, examId, _) = await SeedPureReadingExamAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var requiresPreflight = await engine.RequiresPreflightAsync(examId);
        Assert.False(requiresPreflight);

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 60);
        Assert.NotNull(attempt.PreflightVerifiedAt);

        var status = await engine.GetPreflightStatusAsync(attempt.AttemptId, userId);
        Assert.False(status.RequiresPreflight);
        Assert.True(status.IsVerified);

        // Can immediately enter sitting without redirect or error
        var take = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, null);
        Assert.NotNull(take);
        Assert.Equal(1, take.TotalQuestions);
    }

    [Fact]
    public async Task Unverified_Attempt_Blocks_GetAttemptTake_SaveAnswer_And_SaveDraft()
    {
        var certCode = Unique("PFBL");
        var (certId, examId, q1Id, _) = await SeedExamWithRequireAudioAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 120);
        Assert.Null(attempt.PreflightVerifiedAt);

        // 1. GetAttemptTakeAsync must throw PreflightRequired
        var exTake = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.GetAttemptTakeAsync(attempt.AttemptId, userId, null));
        Assert.Equal(AttemptEngineException.PreflightRequired, exTake.Code);

        // 2. SaveAnswerAsync must throw PreflightRequired
        var exAnswer = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveAnswerAsync(attempt.AttemptId, userId, q1Id, selectedOptionId: 1, isFlagged: false));
        Assert.Equal(AttemptEngineException.PreflightRequired, exAnswer.Code);

        // 3. SaveDraftAsync must throw PreflightRequired
        var exDraft = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveDraftAsync(attempt.AttemptId, userId, q1Id, "test response"));
        Assert.Equal(AttemptEngineException.PreflightRequired, exDraft.Code);
    }

    [Fact]
    public async Task VerifyPreflight_Marks_Verified_Resets_Timer_And_Unlocks_Take_And_Answers()
    {
        var certCode = Unique("PFVR");
        var (certId, examId, q1Id, _) = await SeedExamWithRequireAudioAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 120);
        Assert.Null(attempt.PreflightVerifiedAt);

        // Candidate performs preflight checks and clicks Start Test
        var status = await engine.VerifyPreflightAsync(attempt.AttemptId, userId);
        Assert.True(status.IsVerified);
        Assert.NotNull(status.VerifiedAt);

        // Verify database state: PreflightVerifiedAt is set and timer starts from verification
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var updatedAttempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
        Assert.NotNull(updatedAttempt.PreflightVerifiedAt);
        Assert.True(updatedAttempt.ExpiresAt > DateTimeOffset.UtcNow);

        // Attempt is now unlocked: GetAttemptTakeAsync succeeds
        var take = await engine.GetAttemptTakeAsync(attempt.AttemptId, userId, 1);
        Assert.NotNull(take);
        Assert.Equal(2, take.TotalQuestions);

        // SaveAnswerAsync succeeds
        await using var optDb = DbContextFactory.Create(database.ConnectionString);
        var opt = await optDb.QuestionOptions.FirstAsync(o => o.QuestionId == q1Id);
        var answerResult = await engine.SaveAnswerAsync(attempt.AttemptId, userId, q1Id, selectedOptionId: opt.OptionId, isFlagged: false);
        Assert.NotNull(answerResult);
        Assert.Equal(opt.OptionId, answerResult.SelectedOptionId);
    }

    [Fact]
    public async Task MockExamService_Enforces_Preflight_Gate_And_Verify()
    {
        var certCode = Unique("PFMS");
        var (certId, examId, _, _) = await SeedExamWithRequireAudioAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);

        // Give learner credits
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
              INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), 'admin-{Guid.NewGuid():N}@test.local', 0x01, N'Admin', SYSDATETIMEOFFSET());
            DECLARE @adminId INT = SCOPE_IDENTITY();
            INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
            VALUES ({userId}, 'admin_grant', 5, @adminId, N'Test setup credits');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var learners = new LearnerService(db);
        var service = new MockExamService(db, learners);

        var started = await service.StartAttemptAsync(userId, examId);
        Assert.NotNull(started);

        // Check preflight status via MockExamService
        var preflight = await service.GetPreflightStatusAsync(userId, started.AttemptId);
        Assert.True(preflight.RequiresPreflight);
        Assert.False(preflight.IsVerified);

        // Calling GetAttemptTakeAsync on unverified attempt throws MockExamException with preflight_required
        var ex = await Assert.ThrowsAsync<MockExamException>(() =>
            service.GetAttemptTakeAsync(userId, started.AttemptId, null));
        Assert.Equal(MockExamException.PreflightRequired, ex.Code);

        // Verify preflight
        var verified = await service.VerifyPreflightAsync(userId, started.AttemptId);
        Assert.True(verified.IsVerified);

        // GetAttemptTakeAsync now succeeds
        var take = await service.GetAttemptTakeAsync(userId, started.AttemptId, null);
        Assert.NotNull(take);
    }
}
