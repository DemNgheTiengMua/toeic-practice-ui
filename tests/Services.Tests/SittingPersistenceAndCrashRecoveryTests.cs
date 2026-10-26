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

    private async Task<(int certId, int examId, int skill1Id, int skill2Id, int q1Id, int q2Id, int opt1Id, int opt2Id)> SeedExamAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes) VALUES
              (@cert, 'LIST', N'Listening', 'listening', 1, 30),
              (@cert, 'READ', N'Reading', 'reading', 2, 45);
            DECLARE @sL INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'LIST');
            DECLARE @sR INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'READ');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder, NavigationMode) VALUES
              (@sL, @cert, 'listening', 'P1', N'Photos', 1, 'FULL_SANDBOX'),
              (@sR, @cert, 'reading', 'P5', N'Sentences', 2, 'FULL_SANDBOX');
            DECLARE @secL INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P1');
            DECLARE @secR INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'P5');

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose) VALUES
              (@cert, N'Exam {certCode}', 'published', 75, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder, UnitNumber) VALUES
              (@exam, @cert, @secL, 1, 1),
              (@exam, @cert, @secR, 2, 1);
            DECLARE @gL INT = (SELECT GroupId FROM QuestionGroups WHERE ExamId = @exam AND SectionId = @secL);
            DECLARE @gR INT = (SELECT GroupId FROM QuestionGroups WHERE ExamId = @exam AND SectionId = @secR);

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder) VALUES
              (@gL, @exam, @secL, @sL, N'Listening Q1', 1, 'mcq', 1),
              (@gR, @exam, @secR, @sR, N'Reading Q2', 1, 'mcq', 2);
            DECLARE @q1 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND SectionId = @secL);
            DECLARE @q2 INT = (SELECT QuestionId FROM Questions WHERE ExamId = @exam AND SectionId = @secR);

            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@q1, 'mcq', 'A', N'Opt A', 1),
              (@q1, 'mcq', 'B', N'Opt B', 0),
              (@q2, 'mcq', 'A', N'Opt A', 1),
              (@q2, 'mcq', 'B', N'Opt B', 0);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var skills = await db.Skills.Where(s => s.CertId == cert.CertId).OrderBy(s => s.DisplayOrder).ToListAsync();
        var questions = await db.Questions.Where(q => q.ExamId == exam.ExamId).OrderBy(q => q.DisplayOrder).ToListAsync();
        var opt1 = await db.QuestionOptions.Where(o => o.QuestionId == questions[0].QuestionId && o.Label == "A").SingleAsync();
        var opt2 = await db.QuestionOptions.Where(o => o.QuestionId == questions[1].QuestionId && o.Label == "A").SingleAsync();

        return (cert.CertId, exam.ExamId, skills[0].SkillId, skills[1].SkillId, questions[0].QuestionId, questions[1].QuestionId, opt1.OptionId, opt2.OptionId);
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
        Assert.Equal(q1Id, reloadedAttempt.LastQuestionId);

        var answer = await db.AttemptAnswers.SingleAsync(a => a.AttemptId == attempt.AttemptId && a.QuestionId == q1Id);
        Assert.Equal(opt1Id, answer.SelectedOptionId);
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

        // Verify AnswerSheet contains both questions and restored answers
        Assert.Equal(2, resumeView.AnswerSheet.Count);
        var q1Answer = resumeView.AnswerSheet.Single(a => a.QuestionId == q1Id);
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
