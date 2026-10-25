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
public sealed class PlacementServiceTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private (PlacementService service, LearnerService learners) CreateServices()
    {
        var db = DbContextFactory.Create(database.ConnectionString);
        var learners = new LearnerService(db);
        var service = new PlacementService(db, learners);
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

    private async Task<(int certId, int standardExamId, int quickExamId, int q1Id, int q2Id, int opt1CorrectId, int opt2CorrectId)> SeedPlacementFixtureAsync(string certCode)
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
              (@cert, 'A1', N'Beginner', 0, 200, 1),
              (@cert, 'A2', N'Elementary', 201, 500, 2),
              (@cert, 'B1', N'Intermediate', 501, 750, 3),
              (@cert, 'B2', N'Upper intermediate', 751, 990, 4);

            -- Placement exams
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, QuestionCount) VALUES
              (@cert, N'Standard Placement Test', 'published', 30, 'placement', N'Adaptive across both sections.', 2);
            DECLARE @standard INT = SCOPE_IDENTITY();

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, QuestionCount) VALUES
              (@cert, N'Quick Placement Test', 'published', 15, 'placement', N'A shorter screen.', 1);
            DECLARE @quick INT = SCOPE_IDENTITY();

            -- Other purpose exam (should be ignored by placement catalog)
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, QuestionCount) VALUES
              (@cert, N'Mock Exam 1', 'published', 120, 'mock', N'A full mock exam.', 100);

            -- Draft placement (should be ignored)
            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, QuestionCount) VALUES
              (@cert, N'Draft Placement', 'draft', 30, 'placement', N'Draft.', 2);

            -- Score scale for standard placement
            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
              (@cert, @sL, @standard, 0, 100),
              (@cert, @sL, @standard, 1, 250),
              (@cert, @sR, @standard, 0, 100),
              (@cert, @sR, @standard, 1, 200);

            -- Q1 for Standard (Listening)
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@standard, @cert, @secL, 1);
            DECLARE @grp1 INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@grp1, @standard, @secL, @sL, N'Standard Q1', 1, 'mcq', 1);
            DECLARE @q1 INT = SCOPE_IDENTITY();
            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@q1, 'mcq', 'A', N'Opt A', 1),
              (@q1, 'mcq', 'B', N'Opt B', 0);

            -- Q2 for Standard (Reading)
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@standard, @cert, @secR, 2);
            DECLARE @grp2 INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@grp2, @standard, @secR, @sR, N'Standard Q2', 1, 'mcq', 2);
            DECLARE @q2 INT = SCOPE_IDENTITY();
            INSERT INTO QuestionOptions (QuestionId, QuestionType, Label, Text, IsCorrect) VALUES
              (@q2, 'mcq', 'A', N'Opt A', 0),
              (@q2, 'mcq', 'B', N'Opt B', 1);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var standardExam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId && e.Name == "Standard Placement Test");
        var quickExam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId && e.Name == "Quick Placement Test");
        var questions = await db.Questions.Where(q => q.ExamId == standardExam.ExamId).OrderBy(q => q.DisplayOrder).ToListAsync();
        var q1 = questions[0];
        var q2 = questions[1];

        var opt1 = await db.QuestionOptions.SingleAsync(o => o.QuestionId == q1.QuestionId && o.IsCorrect);
        var opt2 = await db.QuestionOptions.SingleAsync(o => o.QuestionId == q2.QuestionId && o.IsCorrect);

        return (cert.CertId, standardExam.ExamId, quickExam.ExamId, q1.QuestionId, q2.QuestionId, opt1.OptionId, opt2.OptionId);
    }

    [Fact]
    public async Task GetPlacementCatalog_ReturnsOnlyPublishedPlacementExams()
    {
        var certCode = Unique("PC01");
        var (certId, standardExamId, quickExamId, _, _, _, _) = await SeedPlacementFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        var catalog = await service.GetPlacementCatalogAsync(userId);

        Assert.Equal(certCode, catalog.ActiveCert.Code);
        Assert.Null(catalog.CurrentBandCode);
        Assert.Null(catalog.CurrentTotalScore);
        Assert.Null(catalog.ActivePlacementId);

        // Only Standard and Quick published placement tests, not mock or draft
        Assert.Equal(2, catalog.Exams.Count);
        Assert.Contains(catalog.Exams, e => e.ExamId == standardExamId && e.Name == "Standard Placement Test");
        Assert.Contains(catalog.Exams, e => e.ExamId == quickExamId && e.Name == "Quick Placement Test");
    }

    [Fact]
    public async Task StartPlacementTest_IsCompletelyFree_NoCreditsDeducted()
    {
        var certCode = Unique("PC02");
        var (_, standardExamId, _, _, _, _, _) = await SeedPlacementFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 5);
        var (service, _) = CreateServices();

        var info = await service.StartPlacementTestAsync(userId, standardExamId);

        Assert.True(info.PlacementId > 0);
        Assert.True(info.AttemptId > 0);
        Assert.Equal(PlacementStatus.InProgress, info.Status);
        Assert.False(info.IsAutoSubmitted);

        await using var db = DbContextFactory.Create(database.ConnectionString);

        // Verify ExamAttempt
        var attempt = await db.ExamAttempts.Include(a => a.Exam).SingleAsync(a => a.AttemptId == info.AttemptId);
        Assert.Equal("placement", attempt.Exam.Purpose);
        Assert.Equal("in_progress", attempt.Status);

        // Verify PlacementTests row
        var placement = await db.PlacementTests.SingleAsync(p => p.PlacementId == info.PlacementId);
        Assert.Equal("in_progress", placement.Status);
        Assert.Equal("placement", placement.ExamPurpose);
        Assert.Null(placement.TotalScore);
        Assert.Null(placement.ResultBandCode);

        // INVARIANT: Credit wallet is UNTOUCHED (still 5 credits, 0 transactions)
        var balance = await db.vw_UserCreditBalances.Where(b => b.UserId == userId).Select(b => b.Balance).SingleAsync();
        Assert.Equal(5, balance);
        var txCount = await db.CreditTransactions.CountAsync(t => t.AttemptId == info.AttemptId);
        Assert.Equal(0, txCount);

        // Verify questions populated
        var answers = await db.AttemptAnswers.Where(a => a.AttemptId == info.AttemptId).ToListAsync();
        Assert.Equal(2, answers.Count);
    }

    [Fact]
    public async Task StartPlacementTest_WhenAlreadyActive_ResumesExistingPlacement()
    {
        var certCode = Unique("PC03");
        var (_, standardExamId, _, _, _, _, _) = await SeedPlacementFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode, startingCredits: 0);
        var (service, _) = CreateServices();

        var first = await service.StartPlacementTestAsync(userId, standardExamId);
        var second = await service.StartPlacementTestAsync(userId, standardExamId);

        Assert.Equal(first.PlacementId, second.PlacementId);
        Assert.Equal(first.AttemptId, second.AttemptId);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var placementCount = await db.PlacementTests.CountAsync(p => p.UserId == userId);
        Assert.Equal(1, placementCount);
    }

    [Fact]
    public async Task GetPlacementTake_LoadsCurrentQuestionAndNavigator()
    {
        var certCode = Unique("PC04");
        var (_, standardExamId, _, q1Id, _, _, _) = await SeedPlacementFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        var info = await service.StartPlacementTestAsync(userId, standardExamId);
        var take = await service.GetPlacementTakeAsync(userId, info.PlacementId, questionDisplayOrder: 1);

        Assert.Equal(info.PlacementId, take.PlacementId);
        Assert.Equal(info.AttemptId, take.AttemptId);
        Assert.Equal(PlacementStatus.InProgress, take.Status);
        Assert.Equal(2, take.TotalQuestions);
        Assert.Equal(0, take.AnsweredCount);
        Assert.Equal(q1Id, take.CurrentQuestion.QuestionId);
        Assert.Equal(2, take.CurrentQuestion.Options.Count);
        Assert.Equal(2, take.Navigator.Count);
        Assert.True(take.Navigator[0].IsCurrent);
        Assert.False(take.Navigator[1].IsCurrent);
    }

    [Fact]
    public async Task SaveAnswer_UpdatesAnswerSuccessfully()
    {
        var certCode = Unique("PC05");
        var (_, standardExamId, _, q1Id, _, opt1CorrectId, _) = await SeedPlacementFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        var info = await service.StartPlacementTestAsync(userId, standardExamId);
        var result = await service.SaveAnswerAsync(userId, info.PlacementId, q1Id, opt1CorrectId, isFlagged: true);

        Assert.Equal(q1Id, result.QuestionId);
        Assert.Equal(opt1CorrectId, result.SelectedOptionId);
        Assert.True(result.IsFlagged);

        var take = await service.GetPlacementTakeAsync(userId, info.PlacementId, questionDisplayOrder: 1);
        Assert.Equal(1, take.AnsweredCount);
        Assert.Equal(1, take.FlaggedCount);
        Assert.True(take.Navigator[0].IsAnswered);
        Assert.True(take.Navigator[0].IsFlagged);
    }

    [Fact]
    public async Task SubmitPlacementTest_CalculatesScore_MatchesSnapshotTrigger_AssignsBand()
    {
        var certCode = Unique("PC06");
        var (_, standardExamId, _, q1Id, q2Id, opt1CorrectId, opt2CorrectId) = await SeedPlacementFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        var info = await service.StartPlacementTestAsync(userId, standardExamId);

        // Answer Q1 correctly (Listening: raw = 1 -> scaled = 250)
        await service.SaveAnswerAsync(userId, info.PlacementId, q1Id, opt1CorrectId, isFlagged: false);
        await AdvanceAttemptToSecondPhaseAsync(info.AttemptId);
        service = CreateServices().service;
        // Answer Q2 correctly (Reading: raw = 1 -> scaled = 200)
        await service.SaveAnswerAsync(userId, info.PlacementId, q2Id, opt2CorrectId, isFlagged: false);

        // Submit: totalScore should be 250 + 200 = 450 -> falls in A2 (201 to 500)
        var result = await service.SubmitPlacementTestAsync(userId, info.PlacementId, isAutoSubmit: false);

        Assert.Equal(info.PlacementId, result.PlacementId);
        Assert.Equal(450, result.TotalScore);
        Assert.Equal("A2", result.ResultBandCode);
        Assert.Equal("Elementary", result.ResultBandName);
        Assert.Equal(2, result.SkillScores.Count);

        var listSkill = result.SkillScores.Single(s => s.SkillCode == "LIST");
        Assert.Equal(1, listSkill.RawScore);
        Assert.Equal(250, listSkill.ScaledScore);

        var readSkill = result.SkillScores.Single(s => s.SkillCode == "READ");
        Assert.Equal(1, readSkill.RawScore);
        Assert.Equal(200, readSkill.ScaledScore);

        // Verify trigger trg_PlacementTests_SnapshotMatchesAttempt passed and values in DB match!
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var dbPlacement = await db.PlacementTests.SingleAsync(p => p.PlacementId == info.PlacementId);
        var dbAttempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == info.AttemptId);

        Assert.Equal("done", dbPlacement.Status);
        Assert.Equal(450, dbPlacement.TotalScore);
        Assert.Equal("A2", dbPlacement.ResultBandCode);
        Assert.Equal(dbAttempt.TotalScore, dbPlacement.TotalScore);
        Assert.Equal("graded", dbAttempt.Status);
        Assert.Equal("A2", dbAttempt.BandCode);

        // INVARIANT: Still 0 credit transactions!
        var txCount = await db.CreditTransactions.CountAsync(t => t.AttemptId == info.AttemptId);
        Assert.Equal(0, txCount);
    }

    [Fact]
    public async Task GetPlacementResult_AfterSubmit_ReturnsDetailedResult()
    {
        var certCode = Unique("PC07");
        var (_, standardExamId, _, q1Id, q2Id, opt1CorrectId, _) = await SeedPlacementFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        var info = await service.StartPlacementTestAsync(userId, standardExamId);

        // Answer Q1 correctly, Q2 left unanswered (Reading = 0 -> scaled = 100)
        await service.SaveAnswerAsync(userId, info.PlacementId, q1Id, opt1CorrectId, isFlagged: false);
        await service.SubmitPlacementTestAsync(userId, info.PlacementId, isAutoSubmit: false);

        var result = await service.GetPlacementResultAsync(userId, info.PlacementId);

        // Listening: 1/1 (100%), Reading: 0/1 (0%) -> Weaker section is Reading!
        Assert.Equal("Reading", result.WeakerSkillName);
        Assert.NotNull(result.BandNote);
        Assert.True(result.CefrBands.Count >= 4);
    }

    [Fact]
    public async Task TargetBand_GetAndSet_SavesTargetBandForCert()
    {
        var certCode = Unique("PC08");
        var (_, standardExamId, _, q1Id, q2Id, opt1CorrectId, opt2CorrectId) = await SeedPlacementFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        // Take and submit placement first to establish assessed level (A2)
        var info = await service.StartPlacementTestAsync(userId, standardExamId);
        await service.SaveAnswerAsync(userId, info.PlacementId, q1Id, opt1CorrectId, isFlagged: false);
        await AdvanceAttemptToSecondPhaseAsync(info.AttemptId);
        service = CreateServices().service;
        await service.SaveAnswerAsync(userId, info.PlacementId, q2Id, opt2CorrectId, isFlagged: false);
        await service.SubmitPlacementTestAsync(userId, info.PlacementId, isAutoSubmit: false);

        // View target bands
        var targetView = await service.GetTargetBandViewAsync(userId);
        Assert.Equal("A2", targetView.CurrentBandCode);
        Assert.Equal(450, targetView.CurrentTotalScore);
        Assert.Null(targetView.TargetBandCode);
        Assert.True(targetView.AvailableBands.Count >= 4);

        // Set target band to B2
        var setResult = await service.SetTargetBandAsync(userId, "B2");
        Assert.Equal(userId, setResult.UserId);
        Assert.Equal("B2", setResult.TargetBandCode);

        // Reload target bands view
        var updatedView = await service.GetTargetBandViewAsync(userId);
        Assert.Equal("B2", updatedView.TargetBandCode);
        Assert.True(updatedView.AvailableBands.Single(b => b.Code == "B2").IsTarget);

        // Update target band to B1 (upsert works)
        await service.SetTargetBandAsync(userId, "B1");
        var updatedView2 = await service.GetTargetBandViewAsync(userId);
        Assert.Equal("B1", updatedView2.TargetBandCode);
        Assert.True(updatedView2.AvailableBands.Single(b => b.Code == "B1").IsTarget);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var targetBandsInDb = await db.TargetBands.Where(t => t.UserId == userId).ToListAsync();
        Assert.Single(targetBandsInDb);
        Assert.Equal("B1", targetBandsInDb[0].TargetBandCode);
    }

    [Fact]
    public async Task SetTargetBand_ThrowsTargetBandInvalid_WhenBandCodeDoesNotExist()
    {
        var certCode = Unique("PC09");
        await SeedPlacementFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        var ex = await Assert.ThrowsAsync<PlacementException>(() => service.SetTargetBandAsync(userId, "NON_EXISTENT_BAND"));
        Assert.Equal(PlacementException.TargetBandInvalid, ex.Code);
    }

    [Fact]
    public async Task GetPlacementTake_WhenExpired_AutoSubmitsAttemptAndSetsIsAutoSubmitted()
    {
        var certCode = Unique("PC10");
        var (_, standardExamId, _, _, _, _, _) = await SeedPlacementFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        var info = await service.StartPlacementTestAsync(userId, standardExamId);

        // Simulate expired attempt in database
        await database.SqlAsync($"UPDATE ExamAttempts SET ExpiresAt = DATEADD(minute, -10, SYSDATETIMEOFFSET()) WHERE AttemptId = {info.AttemptId};");

        var (touchService, _) = CreateServices();
        var take = await touchService.GetPlacementTakeAsync(userId, info.PlacementId, 1);

        Assert.Equal(PlacementStatus.Done, take.Status);
        Assert.True(take.IsAutoSubmitted);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var dbAttempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == info.AttemptId);
        var dbPlacement = await db.PlacementTests.SingleAsync(p => p.PlacementId == info.PlacementId);

        Assert.True(dbAttempt.IsAutoSubmitted);
        Assert.Equal("graded", dbAttempt.Status);
        Assert.Equal("done", dbPlacement.Status);
    }

    [Fact]
    public async Task SaveAnswer_WhenExpired_AutoSubmitsAttemptAndThrowsPlacementExpired()
    {
        var certCode = Unique("PC11");
        var (_, standardExamId, _, q1Id, _, opt1CorrectId, _) = await SeedPlacementFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        var info = await service.StartPlacementTestAsync(userId, standardExamId);

        // Simulate expired attempt in database
        await database.SqlAsync($"UPDATE ExamAttempts SET ExpiresAt = DATEADD(minute, -10, SYSDATETIMEOFFSET()) WHERE AttemptId = {info.AttemptId};");

        var (touchService, _) = CreateServices();
        var ex = await Assert.ThrowsAsync<PlacementException>(() =>
            touchService.SaveAnswerAsync(userId, info.PlacementId, q1Id, opt1CorrectId, isFlagged: false));
        Assert.Equal(PlacementException.PlacementExpired, ex.Code);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var dbAttempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == info.AttemptId);
        var dbPlacement = await db.PlacementTests.SingleAsync(p => p.PlacementId == info.PlacementId);

        Assert.True(dbAttempt.IsAutoSubmitted);
        Assert.Equal("graded", dbAttempt.Status);
        Assert.Equal("done", dbPlacement.Status);
    }

    [Fact]
    public async Task StartPlacementTest_WhenDifferentExamStarted_AbandonsPreviousPlacementAndAttempt()
    {
        var certCode = Unique("PC12");
        var (_, standardExamId, quickExamId, _, _, _, _) = await SeedPlacementFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        var first = await service.StartPlacementTestAsync(userId, standardExamId);
        var second = await service.StartPlacementTestAsync(userId, quickExamId);

        Assert.NotEqual(first.PlacementId, second.PlacementId);
        Assert.NotEqual(first.AttemptId, second.AttemptId);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var firstPlacement = await db.PlacementTests.SingleAsync(p => p.PlacementId == first.PlacementId);
        var firstAttempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == first.AttemptId);

        Assert.Equal("abandoned", firstPlacement.Status);
        Assert.Equal("abandoned", firstAttempt.Status);

        var secondPlacement = await db.PlacementTests.SingleAsync(p => p.PlacementId == second.PlacementId);
        var secondAttempt = await db.ExamAttempts.SingleAsync(a => a.AttemptId == second.AttemptId);

        Assert.Equal("in_progress", secondPlacement.Status);
        Assert.Equal("in_progress", secondAttempt.Status);
    }

    [Fact]
    public async Task SaveAnswer_ThrowsQuestionNotFound_WhenQuestionNotInPlacement()
    {
        var certCode = Unique("PC13");
        var (_, standardExamId, _, _, _, opt1CorrectId, _) = await SeedPlacementFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        var info = await service.StartPlacementTestAsync(userId, standardExamId);

        var ex = await Assert.ThrowsAsync<PlacementException>(() =>
            service.SaveAnswerAsync(userId, info.PlacementId, 999999, opt1CorrectId, false));
        Assert.Equal(PlacementException.QuestionNotFound, ex.Code);
    }

    [Fact]
    public async Task SaveAnswer_ThrowsInvalidOption_WhenOptionNotInQuestion()
    {
        var certCode = Unique("PC14");
        var (_, standardExamId, _, q1Id, _, _, opt2CorrectId) = await SeedPlacementFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        var info = await service.StartPlacementTestAsync(userId, standardExamId);

        // Passing opt2 (belongs to Q2) when answering Q1 must fail validation
        var ex = await Assert.ThrowsAsync<PlacementException>(() =>
            service.SaveAnswerAsync(userId, info.PlacementId, q1Id, opt2CorrectId, false));
        Assert.Equal(PlacementException.InvalidOption, ex.Code);
    }

    [Fact]
    public async Task GetPlacementResult_ThrowsPlacementNotFinished_WhenPlacementStillInProgress()
    {
        var certCode = Unique("PC15");
        var (_, standardExamId, _, _, _, _, _) = await SeedPlacementFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        var info = await service.StartPlacementTestAsync(userId, standardExamId);

        var ex = await Assert.ThrowsAsync<PlacementException>(() =>
            service.GetPlacementResultAsync(userId, info.PlacementId));
        Assert.Equal(PlacementException.PlacementNotFinished, ex.Code);
    }
    private async Task<(int certId, int examId, int writingQId)> SeedHybridPlacementFixtureAsync(string certCode)
    {
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'WRIT', N'Writing', 'writing', 1);
            DECLARE @sW INT = SCOPE_IDENTITY();

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder) VALUES
              (@sW, @cert, 'writing', 'W1', N'Writing Section', 1);
            DECLARE @secW INT = SCOPE_IDENTITY();

            INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) VALUES
              (@cert, 'A1', N'Beginner', 0, 200, 1),
              (@cert, 'B2', N'Upper intermediate', 201, 990, 2);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose, Description, QuestionCount) VALUES
              (@cert, N'Hybrid Placement Test', 'published', 30, 'placement', N'Includes free response writing.', 1);
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO ScoreScales (CertId, SkillId, ExamId, RawScore, ScaledScore) VALUES
              (@cert, @sW, @exam, 0, 100),
              (@cert, @sW, @exam, 1, 200);

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @secW, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();

            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@grp, @exam, @secW, @sW, N'Diagnostic writing prompt', 1, 'free_response', 1);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId && e.Name == "Hybrid Placement Test");
        var q = await db.Questions.SingleAsync(q => q.ExamId == exam.ExamId);
        return (cert.CertId, exam.ExamId, q.QuestionId);
    }

    [Fact]
    public async Task SaveDraftAsync_SavesDraft_AndGetGradingStatusAsync_ReportsStatus()
    {
        var certCode = Unique("PC16");
        var (_, examId, writingQId) = await SeedHybridPlacementFixtureAsync(certCode);
        var userId = await SeedLearnerAsync(certCode);
        var (service, _) = CreateServices();

        var info = await service.StartPlacementTestAsync(userId, examId);

        var draftResponse = await service.SaveDraftAsync(userId, info.PlacementId, writingQId, "Here is my diagnostic writing response text.");
        Assert.Equal(writingQId, draftResponse.QuestionId);
        Assert.Equal(7, draftResponse.WordCount);

        var status = await service.GetGradingStatusAsync(userId, info.PlacementId);
        Assert.Equal(info.PlacementId, status.PlacementId);
        Assert.NotNull(status.StageDescriptor);
    }
}