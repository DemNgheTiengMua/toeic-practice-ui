using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
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

    /// <summary>
    /// A two-section paper (listening + reading), each holding one position, with audio on the
    /// listening section.
    /// <para>
    /// #269: the positions are authored on the pool, which is what every reader behind this fixture
    /// now resolves - the preflight requirements, the audio URL list and attempt creation all read the
    /// roster. Audio is still an <c>ExamSectionAudio</c> upload here, because a section's master track
    /// is a property of the paper rather than of a stimulus.
    /// </para>
    /// </summary>
    private async Task<(int certId, int examId, int q1Id, int q2Id)> SeedExamWithRequireAudioAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'LIST', N'Listening', 'listening', 1),
              (@cert, 'READ', N'Reading', 'reading', 2);

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, ExpectedQuestionCount, RequireAudio)
            SELECT s.SkillId, @cert, 'listening', 'P1', N'Photos', 1, 1, 1 FROM Skills s WHERE s.CertId = @cert AND s.Code = 'LIST';
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, ExpectedQuestionCount, RequireAudio)
            SELECT s.SkillId, @cert, 'reading', 'P5', N'Sentences', 2, 1, 0 FROM Skills s WHERE s.CertId = @cert AND s.Code = 'READ';

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'LOW', N'Low', 0, 499, 1),
              (@cert, 'HIGH', N'High', 500, 990, 2);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES
              (@cert, N'Exam {certCode}', 'published', 120, 'mock');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var exam = await db.Exams.SingleAsync(e => e.Cert.Code == certCode);
        var sections = await db.Sections.Where(s => s.CertId == exam.CertId)
            .OrderBy(s => s.DisplayOrder).ToListAsync();

        var q1 = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, exam.CertId, sections[0].SectionId, "listening", "Listening question",
            displayOrder: 1, level: "A2");
        var q2 = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, exam.CertId, sections[1].SectionId, "reading", "Reading question",
            displayOrder: 1, level: "A2");

        await database.SqlAsync($"""
            INSERT INTO ExamSectionAudio (ExamId, SectionId, CertId, AudioPath, DurationSec, SizeBytes, UploadedAt)
            VALUES ({exam.ExamId}, {sections[0].SectionId}, {exam.CertId}, '/media/audio/track1.mp3', 120, 1024000, SYSDATETIMEOFFSET());
            """);

        return (exam.CertId, exam.ExamId, q1.ExamItemId, q2.ExamItemId);
    }

    private async Task<(int certId, int examId, int qSpeakingId)> SeedExamWithSpeakingSkillAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'speaking', N'Speaking', 'speaking', 1);

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, ExpectedQuestionCount, RequireAudio)
            SELECT s.SkillId, @cert, 'speaking', 'S1', N'Speaking Part 1', 1, 1, 0 FROM Skills s WHERE s.CertId = @cert AND s.Code = 'speaking';

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'LOW', N'Low', 0, 499, 1),
              (@cert, 'HIGH', N'High', 500, 990, 2);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES
              (@cert, N'Speaking Exam {certCode}', 'published', 45, 'mock');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var exam = await db.Exams.SingleAsync(e => e.Cert.Code == certCode);
        var section = await db.Sections.SingleAsync(s => s.CertId == exam.CertId);

        var q = await PoolFixture.AddProductiveAsync(
            db, exam.ExamId, exam.CertId, section.SectionId, "speaking",
            "Describe your hometown.", displayOrder: 1, level: "A2");

        return (exam.CertId, exam.ExamId, q.ExamItemId);
    }

    private async Task<(int certId, int examId, int qReadingId)> SeedPureReadingExamAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'READ', N'Reading', 'reading', 1);

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, ExpectedQuestionCount, RequireAudio)
            SELECT s.SkillId, @cert, 'reading', 'P5', N'Sentences', 1, 1, 0 FROM Skills s WHERE s.CertId = @cert AND s.Code = 'READ';

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'LOW', N'Low', 0, 499, 1),
              (@cert, 'HIGH', N'High', 500, 990, 2);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES
              (@cert, N'Reading Exam {certCode}', 'published', 60, 'mock');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var exam = await db.Exams.SingleAsync(e => e.Cert.Code == certCode);
        var section = await db.Sections.SingleAsync(s => s.CertId == exam.CertId);

        // A pool MCQ carries four options by construction (CNT-06), so this position offers four where
        // the legacy fixture wrote two. Nothing here counts them.
        var q = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, exam.CertId, section.SectionId, "reading", "Reading sentence",
            displayOrder: 1, level: "A2");

        return (exam.CertId, exam.ExamId, q.ExamItemId);
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
        var opt = await PoolFixture.OptionIdAsync(optDb, examId, q1Id, "A");
        var answerResult = await engine.SaveAnswerAsync(attempt.AttemptId, userId, q1Id, selectedOptionId: opt, isFlagged: false);
        Assert.NotNull(answerResult);
        Assert.Equal(opt, answerResult.SelectedOptionId);
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
