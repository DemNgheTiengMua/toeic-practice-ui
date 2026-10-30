using System;
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
public sealed class MockExamServiceTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private (MockExamService service, LearnerService learners) CreateServices()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var learners = new LearnerService(db);
        var service = new MockExamService(db, learners);
        return (service, learners);
    }

    private Task AdvanceAttemptToSecondPhaseAsync(int attemptId) =>
        database.SqlAsync($"""
            UPDATE a
            SET a.StartedAt = DATEADD(minute, -31, SYSDATETIMEOFFSET()),
                a.ExpiresAt = DATEADD(minute, 29, SYSDATETIMEOFFSET()),
                a.ActiveSkillId = (
                    SELECT TOP 1 s.SkillId
                    FROM Skills s
                    JOIN Sections sec ON sec.SkillId = s.SkillId
                    JOIN Questions q ON q.SectionId = sec.SectionId
                    WHERE q.ExamId = a.ExamId AND s.SkillId <> a.ActiveSkillId
                    ORDER BY s.DisplayOrder
                ),
                a.PhaseDeadlineAt = DATEADD(minute, 30, GETUTCDATE())
            FROM ExamAttempts a
            WHERE a.AttemptId = {attemptId};
            """);

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

    private async Task<(int certId, int examId, int q1Id, int q2Id, int opt1CorrectId, int opt2CorrectId)> SeedMockExamFixtureAsync(string certCode)
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
              (@cert, N'Mock {certCode}', 'published', 120, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();

            -- Scale:
            -- LIST: 0 -> 100, 1 -> 400
            -- READ: 0 -> 100, 1 -> 400
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
              (@cert, @sL, @exam, 0, 100),
              (@cert, @sL, @exam, 1, 400),
              (@cert, @sR, @exam, 0, 100),
              (@cert, @sR, @exam, 1, 400);

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

    private async Task<(int examId, int listeningQuestionId, int writingQuestionId, int listeningOptionId)> SeedHybridMockExamFixtureAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'LIST', N'Listening', 'listening', 1),
              (@cert, 'WRITE', N'Writing', 'writing', 2);
            DECLARE @listeningSkill INT = (SELECT SkillId FROM Skills WHERE CertId=@cert AND Code='LIST');
            DECLARE @writingSkill INT = (SELECT SkillId FROM Skills WHERE CertId=@cert AND Code='WRITE');
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder) VALUES
              (@listeningSkill, @cert, 'listening', 'L1', N'Listening', 1),
              (@writingSkill, @cert, 'writing', 'W1', N'Writing', 1);
            DECLARE @listeningSection INT = (SELECT SectionId FROM Sections WHERE CertId=@cert AND Code='L1');
            DECLARE @writingSection INT = (SELECT SectionId FROM Sections WHERE CertId=@cert AND Code='W1');
            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'LOW', N'Low', 0, 49, 1),
              (@cert, 'HIGH', N'High', 50, 200, 2);
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
            VALUES (@cert, N'Hybrid mock {certCode}', 'published', 60, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
              (@cert, @listeningSkill, @exam, 0, 20),
              (@cert, @listeningSkill, @exam, 1, 80),
              (@cert, @writingSkill, @exam, 0, 60),
              (@cert, @writingSkill, @exam, 1, 100);

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
            VALUES (@exam, @cert, @listeningSection, 1);
            DECLARE @listeningGroup INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@listeningGroup, @exam, @listeningSection, @listeningSkill, N'Choose the correct answer.', 1, 'mcq', 1);
            DECLARE @listeningQuestion INT = SCOPE_IDENTITY();
            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect)
            VALUES (@listeningQuestion, 'mcq', 'A', N'Correct answer', 1);

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder)
            VALUES (@exam, @cert, @writingSection, 2);
            DECLARE @writingGroup INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, MinWords, MaxWords, DisplayOrder)
            VALUES (@writingGroup, @exam, @writingSection, @writingSkill, N'Write a short response.', 1, 'free_response', 3, 100, 2);
            DECLARE @writingQuestion INT = SCOPE_IDENTITY();
            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder)
            VALUES (@cert, @writingSkill, 'CONTENT', N'Content', 10, 1, 1);
            DECLARE @criterion INT = SCOPE_IDENTITY();
            INSERT INTO SectionCriteria (SectionId, CriterionId, SkillId, MaxScore)
            VALUES (@writingSection, @criterion, @writingSkill, 10);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var examId = await db.Exams
            .Where(e => e.Cert.Code == certCode)
            .Select(e => e.ExamId)
            .SingleAsync();
        var questions = await db.Questions.Where(q => q.ExamId == examId).OrderBy(q => q.DisplayOrder).ToListAsync();
        var optionId = await db.QuestionOptions
            .Where(o => o.QuestionId == questions[0].QuestionId && o.IsCorrect)
            .Select(o => o.OptionId)
            .SingleAsync();
        return (examId, questions[0].QuestionId, questions[1].QuestionId, optionId);
    }

    [Fact]
    public async Task StartAttempt_WhenInsufficientCredits_ThrowsInsufficientCreditsBeforeCharge()
    {
        var certCode = Unique("MC01");
        var (_, examId, _, _, _, _) = await SeedMockExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 0);
        var (service, _) = CreateServices();

        var ex = await Assert.ThrowsAsync<MockExamException>(() => service.StartAttemptAsync(userId, examId));
        Assert.Equal(MockExamException.InsufficientCredits, ex.Code);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var attempts = await db.ExamAttempts.Where(a => a.UserId == userId).ToListAsync();
        var txns = await db.CreditTransactions.Where(t => t.UserId == userId).ToListAsync();

        Assert.Empty(attempts);
        Assert.Empty(txns);
    }

    [Fact]
    public async Task HybridMock_SubmissionQueuesWritingAndFinalizesCompositeAfterAiGrading()
    {
        var certCode = Unique("HYB");
        var fixture = await SeedHybridMockExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 3);
        var (service, _) = CreateServices();

        var attempt = await service.StartAttemptAsync(userId, fixture.examId);
        var resumed = await service.StartAttemptAsync(userId, fixture.examId);
        Assert.Equal(attempt.AttemptId, resumed.AttemptId);

        // Writing is the next phase: while Listening is active, asking for its question returns the
        // active Listening question instead (the one-way phase lock, f029d82).
        var locked = await service.GetAttemptTakeAsync(userId, attempt.AttemptId, questionDisplayOrder: 2);
        Assert.Equal("mcq", locked.CurrentQuestion.QuestionType);
        Assert.Equal(1, locked.CurrentQuestion.DisplayOrder);

        await service.SaveAnswerAsync(
            userId,
            attempt.AttemptId,
            fixture.listeningQuestionId,
            fixture.listeningOptionId,
            isFlagged: false);
        await AdvanceAttemptToSecondPhaseAsync(attempt.AttemptId);
        (service, _) = CreateServices();

        var take = await service.GetAttemptTakeAsync(userId, attempt.AttemptId, questionDisplayOrder: 2);
        Assert.Equal("free_response", take.CurrentQuestion.QuestionType);
        Assert.Equal(3, take.CurrentQuestion.MinWords);
        Assert.Equal(string.Empty, take.CurrentQuestion.ResponseText);

        const string responseText = "A clear response with enough detail.";
        await service.SaveAnswerAsync(
            userId,
            attempt.AttemptId,
            fixture.writingQuestionId,
            selectedOptionId: null,
            isFlagged: false,
            responseText: responseText);

        var interim = await service.SubmitAttemptAsync(userId, attempt.AttemptId, isAutoSubmit: false);
        Assert.Equal(AttemptStatus.Submitted, interim.Status);
        Assert.Null(interim.BandCode);
        Assert.Equal(1, interim.FreeResponseCount);
        Assert.Equal(0, interim.GradedFreeResponseCount);
        Assert.Single(interim.SkillScores);
        Assert.Equal("LIST", interim.SkillScores[0].SkillCode);

        int freeResponseId;
        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var response = await db.FreeResponses.SingleAsync(fr => fr.AttemptId == attempt.AttemptId);
            freeResponseId = response.FreeResponseId;
            Assert.Equal("pending_ai", response.Status);
            Assert.Equal(responseText, response.ResponseText);
            Assert.NotNull(response.SubmittedAt);

            var grading = await db.AiGradings.SingleAsync(g => g.FreeResponseId == response.FreeResponseId);
            Assert.Equal("pending", grading.Status);

            var balance = await db.vw_UserCreditBalances
                .Where(b => b.UserId == userId)
                .Select(b => b.Balance)
                .SingleAsync();
            Assert.Equal(2, balance);
        }

        await using (var gradingDb = DbContextFactory.Create(database.ConnectionString))
        {
            var processor = new AiGradingProcessor(gradingDb, new FakeAiGrader());
            Assert.True(await processor.GradeFreeResponseAsync(freeResponseId));
        }

        var finalResult = await service.GetAttemptResultAsync(userId, attempt.AttemptId);
        Assert.Equal(AttemptStatus.Graded, finalResult.Status);
        Assert.Equal(1, finalResult.GradedFreeResponseCount);
        Assert.Equal(2, finalResult.SkillScores.Count);
        Assert.Equal("HIGH", finalResult.BandCode);
        Assert.True(finalResult.TotalScore > 0);
    }

    [Fact]
    public async Task StartAttempt_WhenCreditsSufficient_ChargesOneCreditAndSetsServerCountdown()
    {
        var certCode = Unique("MC02");
        var (_, examId, _, _, _, _) = await SeedMockExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 5);
        var (service, _) = CreateServices();

        var attempt = await service.StartAttemptAsync(userId, examId);

        Assert.Equal(AttemptStatus.InProgress, attempt.Status);
        Assert.False(attempt.IsAutoSubmitted);
        Assert.True(attempt.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(115));
        Assert.True(attempt.RemainingSeconds > 7100);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var dbAttempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
        Assert.Equal("in_progress", dbAttempt.Status);

        var balance = await db.vw_UserCreditBalances.Where(b => b.UserId == userId).Select(b => b.Balance).SingleAsync();
        Assert.Equal(4, balance); // 5 - 1 = 4

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
    public async Task StartAttempt_WhenAlreadyActive_ResumesWithoutDoubleCharging()
    {
        var certCode = Unique("MC03");
        var (_, examId, _, _, _, _) = await SeedMockExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 3);
        var (service, _) = CreateServices();

        var first = await service.StartAttemptAsync(userId, examId);
        var second = await service.StartAttemptAsync(userId, examId);

        Assert.Equal(first.AttemptId, second.AttemptId);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var balance = await db.vw_UserCreditBalances.Where(b => b.UserId == userId).Select(b => b.Balance).SingleAsync();
        Assert.Equal(2, balance); // only charged once
    }

    [Fact]
    public async Task SaveAnswer_PersistsAnswerAndFlagState()
    {
        var certCode = Unique("MC04");
        var (_, examId, q1Id, _, opt1Id, _) = await SeedMockExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 2);
        var (service, _) = CreateServices();

        var attempt = await service.StartAttemptAsync(userId, examId);
        var saveResult = await service.SaveAnswerAsync(userId, attempt.AttemptId, q1Id, opt1Id, isFlagged: true);

        Assert.Equal(q1Id, saveResult.QuestionId);
        Assert.Equal(opt1Id, saveResult.SelectedOptionId);
        Assert.True(saveResult.IsFlagged);

        // Verify take view restores answer and flagged state
        var take = await service.GetAttemptTakeAsync(userId, attempt.AttemptId, questionDisplayOrder: 1);
        Assert.Equal(1, take.AnsweredCount);
        Assert.Equal(1, take.FlaggedCount);
        Assert.Equal(opt1Id, take.CurrentQuestion.SelectedOptionId);
        Assert.True(take.CurrentQuestion.IsFlagged);
        Assert.True(take.Navigator[0].IsAnswered);
        Assert.True(take.Navigator[0].IsFlagged);
    }

    [Fact]
    public async Task Reload_DoesNotResetCountdown()
    {
        var certCode = Unique("MC05");
        var (_, examId, _, _, _, _) = await SeedMockExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 2);
        var (service, _) = CreateServices();

        var attempt = await service.StartAttemptAsync(userId, examId);

        var firstTake = await service.GetAttemptTakeAsync(userId, attempt.AttemptId, null);
        var secondTake = await service.GetAttemptTakeAsync(userId, attempt.AttemptId, null);

        Assert.Equal(firstTake.ExpiresAt, secondTake.ExpiresAt);
        Assert.True(secondTake.RemainingSeconds <= firstTake.RemainingSeconds);
    }

    [Fact]
    public async Task AutoSubmit_OnNextTouchPastDeadline_SetsIsAutoSubmittedAndScores()
    {
        var certCode = Unique("MC06");
        var (_, examId, q1Id, _, opt1Id, _) = await SeedMockExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 2);
        var (service, _) = CreateServices();

        var attempt = await service.StartAttemptAsync(userId, examId);

        // Manually expire the attempt in the database
        await database.SqlAsync($"""
            UPDATE ExamAttempts
            SET ExpiresAt = DATEADD(MINUTE, -10, SYSDATETIMEOFFSET())
            WHERE AttemptId = {attempt.AttemptId};
            """);

        // Next touch via SaveAnswer (on a subsequent request) should fail with AttemptExpired and auto-submit
        var (touchService, _) = CreateServices();
        var ex = await Assert.ThrowsAsync<MockExamException>(() =>
            touchService.SaveAnswerAsync(userId, attempt.AttemptId, q1Id, opt1Id, isFlagged: false));
        Assert.Equal(MockExamException.AttemptExpired, ex.Code);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var dbAttempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
        Assert.Equal("graded", dbAttempt.Status);
        Assert.True(dbAttempt.IsAutoSubmitted);
        Assert.NotNull(dbAttempt.SubmittedAt);
        Assert.NotNull(dbAttempt.TotalScore);
        Assert.NotNull(dbAttempt.BandCode);
    }

    [Fact]
    public async Task CancelAttempt_EndsAttemptWithoutResult()
    {
        var certCode = Unique("MC07");
        var (_, examId, _, _, _, _) = await SeedMockExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 2);
        var (service, _) = CreateServices();

        var attempt = await service.StartAttemptAsync(userId, examId);
        await service.CancelAttemptAsync(userId, attempt.AttemptId);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var dbAttempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);

        Assert.Equal("abandoned", dbAttempt.Status);
        Assert.Null(dbAttempt.SubmittedAt);
        Assert.Null(dbAttempt.TotalScore);
        Assert.Null(dbAttempt.BandCode);

        // Getting result of abandoned attempt throws AttemptNotActive
        await Assert.ThrowsAsync<MockExamException>(() =>
            service.GetAttemptResultAsync(userId, attempt.AttemptId));
    }

    [Fact]
    public async Task SubmitAttempt_ComputesScaledScoresAndSnapshotsBand()
    {
        var certCode = Unique("MC08");
        var (_, examId, q1Id, q2Id, opt1CorrectId, opt2CorrectId) = await SeedMockExamFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 2);
        var (service, _) = CreateServices();

        var attempt = await service.StartAttemptAsync(userId, examId);

        // Answer Q1 correctly (Listening) and Q2 correctly (Reading)
        await service.SaveAnswerAsync(userId, attempt.AttemptId, q1Id, opt1CorrectId, isFlagged: false);
        await AdvanceAttemptToSecondPhaseAsync(attempt.AttemptId);
        service = CreateServices().service;
        await service.SaveAnswerAsync(userId, attempt.AttemptId, q2Id, opt2CorrectId, isFlagged: false);

        var result = await service.SubmitAttemptAsync(userId, attempt.AttemptId, isAutoSubmit: false);

        Assert.Equal(AttemptStatus.Graded, result.Status);
        Assert.False(result.IsAutoSubmitted);
        Assert.Equal(800, result.TotalScore); // Listening 400 + Reading 400 = 800
        Assert.Equal("HIGH", result.BandCode); // 800 is in HIGH (500-990)
        Assert.Equal(2, result.SkillScores.Count);

        var listeningScore = result.SkillScores.Single(s => s.SkillCode == "LIST");
        Assert.Equal(1, listeningScore.RawScore);
        Assert.Equal(400, listeningScore.ScaledScore);

        var readingScore = result.SkillScores.Single(s => s.SkillCode == "READ");
        Assert.Equal(1, readingScore.RawScore);
        Assert.Equal(400, readingScore.ScaledScore);

        // Verify snapshotted state in DB
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var dbAttempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == attempt.AttemptId);
        Assert.Equal(800, dbAttempt.TotalScore);
        Assert.Equal("HIGH", dbAttempt.BandCode);
        Assert.Equal("graded", dbAttempt.Status);
        Assert.NotNull(dbAttempt.SubmittedAt);
    }

    [Fact]
    public async Task History_Lists_Attempts_For_Active_Certificate_Only()
    {
        var certCode1 = Unique("C1");
        var certCode2 = Unique("C2");
        var (cert1Id, exam1Id, q1A, q1B, o1A, o1B) = await SeedMockExamFixtureAsync(certCode1);
        var (cert2Id, exam2Id, q2A, q2B, o2A, o2B) = await SeedMockExamFixtureAsync(certCode2);

        var userId = await SeedLearnerAsync(certCode1, startingCredits: 5);
        var (service, learners) = CreateServices();

        // Complete an attempt for Cert 1
        var attempt1 = await service.StartAttemptAsync(userId, exam1Id);
        await service.SaveAnswerAsync(userId, attempt1.AttemptId, q1A, o1A, false);
        await service.SubmitAttemptAsync(userId, attempt1.AttemptId, false);

        // Switch active cert to Cert 2, complete an attempt for Cert 2
        await learners.SwitchCertificateAsync(userId, certCode2);
        var attempt2 = await service.StartAttemptAsync(userId, exam2Id);
        await service.SaveAnswerAsync(userId, attempt2.AttemptId, q2A, o2A, false);
        await service.SubmitAttemptAsync(userId, attempt2.AttemptId, false);

        // While active cert is Cert 2, history should only list attempt2
        var historyCert2 = await service.GetExamHistoryAsync(userId);
        Assert.Equal(certCode2, historyCert2.Certificate.Code);
        Assert.Single(historyCert2.Attempts);
        Assert.Equal(attempt2.AttemptId, historyCert2.Attempts[0].AttemptId);

        // Switch back to Cert 1, history should only list attempt1
        await learners.SwitchCertificateAsync(userId, certCode1);
        var historyCert1 = await service.GetExamHistoryAsync(userId);
        Assert.Equal(certCode1, historyCert1.Certificate.Code);
        Assert.Single(historyCert1.Attempts);
        Assert.Equal(attempt1.AttemptId, historyCert1.Attempts[0].AttemptId);
    }

    [Fact]
    public async Task Review_Is_Available_Only_For_Learners_Own_Finished_Attempts()
    {
        var certCode = Unique("REV");
        var (certId, examId, q1Id, q2Id, opt1Id, opt2Id) = await SeedMockExamFixtureAsync(certCode);

        var user1 = await SeedLearnerAsync(certCode, startingCredits: 5);
        var user2 = await SeedLearnerAsync(certCode, startingCredits: 5);
        var (service, _) = CreateServices();

        var attempt = await service.StartAttemptAsync(user1, examId);

        // 1. In-progress attempt cannot be reviewed (AttemptNotFinished)
        var exNotFinished = await Assert.ThrowsAsync<MockExamException>(
            () => service.GetAttemptReviewAsync(user1, attempt.AttemptId));
        Assert.Equal(MockExamException.AttemptNotFinished, exNotFinished.Code);

        // 2. Another learner cannot review it (AttemptNotFound)
        var exOtherUser = await Assert.ThrowsAsync<MockExamException>(
            () => service.GetAttemptReviewAsync(user2, attempt.AttemptId));
        Assert.Equal(MockExamException.AttemptNotFound, exOtherUser.Code);

        // 3. Complete and submit attempt
        await service.SaveAnswerAsync(user1, attempt.AttemptId, q1Id, opt1Id, false);
        await service.SubmitAttemptAsync(user1, attempt.AttemptId, false);

        // 4. Now user1 can review their own finished attempt
        var review = await service.GetAttemptReviewAsync(user1, attempt.AttemptId);
        Assert.NotNull(review);
        Assert.Equal(attempt.AttemptId, review.AttemptId);
        Assert.Equal(2, review.Questions.Count);

        // 5. User2 still cannot review user1's finished attempt
        var exStillForbidden = await Assert.ThrowsAsync<MockExamException>(
            () => service.GetAttemptReviewAsync(user2, attempt.AttemptId));
        Assert.Equal(MockExamException.AttemptNotFound, exStillForbidden.Code);
    }

    [Fact]
    public async Task Question_Navigator_Reflects_Answered_Flagged_And_Unanswered_States()
    {
        var certCode = Unique("NAV");
        var (certId, examId, q1Id, q2Id, opt1Id, opt2Id) = await SeedMockExamFixtureAsync(certCode);

        var userId = await SeedLearnerAsync(certCode, startingCredits: 5);
        var (service, _) = CreateServices();

        var attempt = await service.StartAttemptAsync(userId, examId);

        // Answer Q1 and flag it
        await service.SaveAnswerAsync(userId, attempt.AttemptId, q1Id, opt1Id, isFlagged: true);
        // Leave Q2 unanswered but not flagged
        // Submit
        await service.SubmitAttemptAsync(userId, attempt.AttemptId, false);

        var review = await service.GetAttemptReviewAsync(userId, attempt.AttemptId);
        Assert.Equal(2, review.Navigator.Count);

        var nav1 = review.Navigator.Single(n => n.QuestionId == q1Id);
        Assert.True(nav1.IsAnswered);
        Assert.True(nav1.IsFlagged);
        Assert.True(nav1.IsCorrect);

        var nav2 = review.Navigator.Single(n => n.QuestionId == q2Id);
        Assert.False(nav2.IsAnswered);
        Assert.False(nav2.IsFlagged);
        Assert.Null(nav2.IsCorrect);

        Assert.Equal(1, review.AnsweredCount);
        Assert.Equal(1, review.BlankCount);
        Assert.Equal(1, review.FlaggedCount);
        Assert.Equal(1, review.CorrectCount);
        Assert.Equal(0, review.IncorrectCount);
    }

    [Fact]
    public async Task GetExamInstructions_lists_only_sections_with_questions_in_this_exam_and_their_actual_counts()
    {
        var certCode = Unique("MC76");
        var (certId, examId, _, _, _, _) = await SeedMockExamFixtureAsync(certCode);
        // The certificate also has a Speaking section this exam has no questions in, and P1's
        // certificate-wide expected count differs from the exam's real count.
        await database.SqlAsync($"""
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes)
            VALUES ({certId}, 'SPK', N'Speaking', 'speaking', 3, 20);
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder)
            VALUES ((SELECT SkillId FROM Skills WHERE CertId = {certId} AND Code = 'SPK'), {certId}, 'speaking', 'S1', N'Read aloud', 3);
            UPDATE Sections SET ExpectedQuestionCount = 6 WHERE CertId = {certId} AND Code = 'P1';
            """);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        var instructions = await service.GetExamInstructionsAsync(userId, examId);

        Assert.Equal(["P1", "P5"], instructions.Sections.Select(s => s.SectionCode));
        Assert.All(instructions.Sections, s => Assert.Equal(1, s.QuestionCount));
    }
}
