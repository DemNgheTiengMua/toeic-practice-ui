using System;
using System.Collections.Generic;
using System.Linq;
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
public sealed class SittingPersistenceAndCrashRecoveryTests(DatabaseFixture database)
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

    /// <summary>
    /// One paper with a Listening and a Reading position, authored on the pool.
    /// <para>
    /// <b>#272: migrated.</b> The ids this file's tests address are <c>ExamItemId</c>s - a position is an
    /// <c>ExamItems</c> row - so the option ids are the projection's (a pinned version stores labels
    /// A-D, not option rows) and an answer stores <c>SelectedLabel</c>.
    /// </para>
    /// </summary>
    private async Task<(int certId, int examId, int skill1Id, int skill2Id, int q1Id, int q2Id, int opt1Id, int opt2Id)> SeedExamAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes, MinScaledScore, MaxScaledScore, ScoreStep) VALUES
              (@cert, 'LIST', N'Listening', 'listening', 1, 30, 0, 100, 5),
              (@cert, 'READ', N'Reading', 'reading', 2, 45, 0, 100, 5);
            DECLARE @sL INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'LIST');
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, ExpectedQuestionCount, NavigationMode) VALUES
              (@sL, @cert, 'listening', 'P1', N'Photos', 1, 1, 'FULL_SANDBOX'),
              (@sR, @cert, 'reading', 'P5', N'Sentences', 2, 1, 'FULL_SANDBOX');

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES
              (@cert, N'Exam {certCode}', 'published', 75, 'mock');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var skills = await db.Skills.Where(s => s.CertId == cert.CertId).OrderBy(s => s.DisplayOrder).ToListAsync();
        var secL = await db.Sections.SingleAsync(s => s.CertId == cert.CertId && s.Code == "P1");
        var secR = await db.Sections.SingleAsync(s => s.CertId == cert.CertId && s.Code == "P5");

        var q1 = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, cert.CertId, secL.SectionId, "listening", "Listening Q1",
            displayOrder: 1, keyLabel: "A", level: "A2");
        var q2 = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, cert.CertId, secR.SectionId, "reading", "Reading Q2",
            displayOrder: 2, keyLabel: "A", level: "A2");

        var opt1 = await PoolFixture.OptionIdAsync(db, exam.ExamId, q1.ExamItemId, "A");
        var opt2 = await PoolFixture.OptionIdAsync(db, exam.ExamId, q2.ExamItemId, "A");

        return (cert.CertId, exam.ExamId, skills[0].SkillId, skills[1].SkillId,
            q1.ExamItemId, q2.ExamItemId, opt1, opt2);
    }

    [Fact]
    public async Task AuthorizationCode_IsGenerated_AndUnique_WhenAttemptCreated()
    {
        var certCode = Unique("AUTH1");
        var (certId, examId, _, _, _, _, _, _) = await SeedExamAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt1 = await engine.CreateAttemptAsync(userId, certId, examId, 75);
        var attempt2 = await engine.CreateAttemptAsync(userId, certId, examId, 75);

        Assert.NotNull(attempt1.AuthorizationCode);
        Assert.StartsWith("EX-", attempt1.AuthorizationCode);
        Assert.Equal(9, attempt1.AuthorizationCode.Length); // EX- + 6 chars
        Assert.NotNull(attempt2.AuthorizationCode);
        Assert.StartsWith("EX-", attempt2.AuthorizationCode);
        Assert.NotEqual(attempt1.AuthorizationCode, attempt2.AuthorizationCode);

        // Verification via Take View
        var takeView = await engine.GetAttemptTakeAsync(attempt1.AttemptId, userId, null);
        Assert.Equal(attempt1.AuthorizationCode, takeView.AuthorizationCode);
    }

    [Fact]
    public async Task SaveSittingSyncAsync_PersistsBatchAnswers_AudioOffset_AndNotes()
    {
        var certCode = Unique("SYNC1");
        var (certId, examId, skill1Id, _, q1Id, _, opt1Id, _) = await SeedExamAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 75);

        var syncRequest = new SittingSyncRequest(
            Answers: [new BatchAnswerItem(q1Id, opt1Id, IsFlagged: true)],
            LastAudioOffsetMs: 14250,
            CandidateNotesJson: "{\"notes\":[{\"q\":1,\"text\":\"flagged question\"}]}",
            LastQuestionId: q1Id);

        var syncResult = await engine.SaveSittingSyncAsync(attempt.AttemptId, syncRequest, userId);

        Assert.Equal(attempt.AttemptId, syncResult.AttemptId);
        Assert.Equal(1, syncResult.AnswersSavedCount);
        Assert.Equal(14250, syncResult.LastAudioOffsetMs);
        Assert.False(syncResult.IsAutoSubmitted);
        Assert.True(syncResult.RemainingSeconds > 0);

        // Verify in DB
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var reloadedAttempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
        Assert.Equal(14250, reloadedAttempt.LastAudioOffsetMs);
        Assert.Equal("{\"notes\":[{\"q\":1,\"text\":\"flagged question\"}]}", reloadedAttempt.CandidateNotesJson);
        Assert.Equal(q1Id, reloadedAttempt.CurrentExamItemId);

        // #272: a pool answer is addressed by its roster position and records the LABEL it picked -
        // a pinned version stores A-D, not option rows, so there is no option id to store here.
        var answer = await db.AttemptAnswers.SingleAsync(a => a.AttemptId == attempt.AttemptId && a.ExamItemId == q1Id);
        Assert.Equal("A", answer.SelectedLabel);
        Assert.True(answer.IsFlagged);
    }

    [Fact]
    public async Task SaveSittingSyncAsync_WhenQuestionInInactiveSkill_ThrowsWrongPhase()
    {
        var certCode = Unique("SYNC2");
        var (certId, examId, _, _, _, q2Id, _, opt2Id) = await SeedExamAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 75);

        // q2 is in Reading (inactive skill)
        var syncRequest = new SittingSyncRequest(
            Answers: [new BatchAnswerItem(q2Id, opt2Id, IsFlagged: false)]);

        var ex = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.SaveSittingSyncAsync(attempt.AttemptId, syncRequest, userId));

        Assert.Equal(AttemptEngineException.WrongPhase, ex.Code);
    }

    [Fact]
    public async Task SaveSittingSyncAsync_WhenWallClockExpired_AutoSubmitsAndThrowsAttemptExpired()
    {
        var certCode = Unique("SYNC3");
        var (certId, examId, _, _, q1Id, _, opt1Id, _) = await SeedExamAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 75);

        // Expire attempt
        await database.SqlAsync($"UPDATE ExamAttempts SET ExpiresAt = DATEADD(minute, -10, SYSDATETIMEOFFSET()) WHERE AttemptId = {attempt.AttemptId}");

        var syncRequest = new SittingSyncRequest(
            Answers: [new BatchAnswerItem(q1Id, opt1Id, IsFlagged: false)],
            LastAudioOffsetMs: 5000);

        var ex = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            CreateEngine().SaveSittingSyncAsync(attempt.AttemptId, syncRequest, userId));

        Assert.Equal(AttemptEngineException.AttemptExpired, ex.Code);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var reloadedAttempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
        Assert.True(reloadedAttempt.Status is "submitted" or "graded");
        Assert.True(reloadedAttempt.IsAutoSubmitted);
    }

    [Fact]
    public async Task ResumeAttemptByCodeAsync_RestoresAttemptState_AndCompleteAnswerSheet()
    {
        var certCode = Unique("RES1");
        var (certId, examId, skill1Id, _, q1Id, q2Id, opt1Id, _) = await SeedExamAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 75);
        var authCode = attempt.AuthorizationCode!;

        // Save progress via sync
        await engine.SaveSittingSyncAsync(attempt.AttemptId, new SittingSyncRequest(
            Answers: [new BatchAnswerItem(q1Id, opt1Id, IsFlagged: true)],
            LastAudioOffsetMs: 25000,
            CandidateNotesJson: "{\"highlights\":[{\"q\":1,\"tokens\":[2,3]}]}",
            LastQuestionId: q1Id), userId);

        // Candidate crashes tab, then resumes with authCode (even lowercase)
        var resumeView = await engine.ResumeAttemptByCodeAsync(authCode.ToLowerInvariant(), userId);

        Assert.Equal(attempt.AttemptId, resumeView.AttemptId);
        Assert.Equal(examId, resumeView.ExamId);
        Assert.Equal(authCode, resumeView.AuthorizationCode);
        Assert.Equal(skill1Id, resumeView.ActiveSkillId);
        Assert.Equal(1, resumeView.ActiveUnitNumber);
        Assert.Equal(q1Id, resumeView.LastQuestionId);
        Assert.Equal(25000, resumeView.LastAudioOffsetMs);
        Assert.Equal("{\"highlights\":[{\"q\":1,\"tokens\":[2,3]}]}", resumeView.CandidateNotesJson);
        Assert.True(resumeView.RemainingSeconds > 0);

        // Verify AnswerSheet contains both positions and restored answers
        Assert.Equal(2, resumeView.AnswerSheet.Count);
        var q1Answer = resumeView.AnswerSheet.Single(a => a.QuestionId == q1Id);
        // #272: the sheet still hands back the PROJECTED option id (a pool answer decodes its stored
        // label back to the id the sitting rendered), so the wire value is unchanged.
        Assert.Equal(opt1Id, q1Answer.SelectedOptionId);
        Assert.True(q1Answer.IsFlagged);

        var q2Answer = resumeView.AnswerSheet.Single(a => a.QuestionId == q2Id);
        Assert.Null(q2Answer.SelectedOptionId);
        Assert.False(q2Answer.IsFlagged);
    }

    [Fact]
    public async Task ResumeAttemptByCodeAsync_WhenExpired_AutoSubmitsAndThrowsAttemptExpired()
    {
        var certCode = Unique("RES2");
        var (certId, examId, _, _, _, _, _, _) = await SeedExamAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(userId, certId, examId, 75);
        var authCode = attempt.AuthorizationCode!;

        // Expire attempt
        await database.SqlAsync($"UPDATE ExamAttempts SET ExpiresAt = DATEADD(minute, -10, SYSDATETIMEOFFSET()) WHERE AttemptId = {attempt.AttemptId}");

        var ex = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            CreateEngine().ResumeAttemptByCodeAsync(authCode, userId));

        Assert.Equal(AttemptEngineException.AttemptExpired, ex.Code);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var reloadedAttempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
        Assert.True(reloadedAttempt.Status is "submitted" or "graded");
        Assert.True(reloadedAttempt.IsAutoSubmitted);
    }

    [Fact]
    public async Task ResumeAttemptByCodeAsync_WhenInvalidCode_ThrowsAttemptNotFound()
    {
        var certCode = Unique("RES3");
        var (certId, examId, _, _, _, _, _, _) = await SeedExamAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var ex = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.ResumeAttemptByCodeAsync("EX-NONEXISTENT", userId));

        Assert.Equal(AttemptEngineException.AttemptNotFound, ex.Code);
    }

    [Fact]
    public async Task ResumeAttemptByCodeAsync_WhenBelongsToDifferentUser_ThrowsAttemptNotFound()
    {
        var certCode = Unique("RES4");
        var (certId, examId, _, _, _, _, _, _) = await SeedExamAsync(certCode);
        var user1Id = await SeedLearnerAsync(certCode);
        var user2Id = await SeedLearnerAsync(certCode);
        var engine = CreateEngine();

        var attempt = await engine.CreateAttemptAsync(user1Id, certId, examId, 75);
        var authCode = attempt.AuthorizationCode!;

        // User 2 attempts to resume User 1's code
        var ex = await Assert.ThrowsAsync<AttemptEngineException>(() =>
            engine.ResumeAttemptByCodeAsync(authCode, user2Id));

        Assert.Equal(AttemptEngineException.AttemptNotFound, ex.Code);
    }
}
