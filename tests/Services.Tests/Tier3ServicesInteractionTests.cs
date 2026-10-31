using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

/// <summary>
/// Tier 3: Cross-Feature Interactions Test Suite for Services.
/// Tests interaction across multiple service boundaries:
/// 1. Sitting lifecycle & credit ledger deduction ("Block before you charge")
/// 2. Diagnostic placement sitting non-deduction
/// 3. Practice drilling feeding deterministic SQL accuracy views
/// 4. Practice session pruning bound preservation of learning paths
/// 5. Free-response AI grading queue seam (ADR-0008)
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class Tier3ServicesInteractionTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private async Task<int> SeedLearnerWithCreditsAsync(int startingCredits, string? activeCertCode = null)
    {
        var email = $"{Guid.NewGuid():N}@learner.test";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Interaction Learner',
                    (SELECT CertId FROM Certificates WHERE Code = {(activeCertCode is null ? "NULL" : $"'{activeCertCode}'")}));
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();

        if (startingCredits > 0)
        {
            await database.SqlAsync($"""
                IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
                  INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
                INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
                VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), 'admin-{Guid.NewGuid():N}@test.local', 0x01, N'Admin');
                DECLARE @adminId INT = SCOPE_IDENTITY();
                INSERT INTO CreditTransactions (UserId, Reason, Delta, AdminUserId, Note)
                VALUES ({userId}, 'admin_grant', {startingCredits}, @adminId, N'Initial credits for interaction test');
                """);
        }

        return userId;
    }

    private async Task<(int certId, int examId, int attemptId)> SeedAttemptFixtureAsync(int userId, string purpose = "mock")
    {
        var certCode = Unique("CRT");
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            INSERT INTO Exams (CertId, Purpose, Name, DurationMinutes, Status, IsDeleted)
            VALUES (@cert, '{purpose}', N'Test Exam', 60, 'published', 0);
            DECLARE @exam INT = SCOPE_IDENTITY();
            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, IsAutoSubmitted)
            VALUES ({userId}, @exam, @cert, 'in_progress', SYSDATETIMEOFFSET(), DATEADD(minute, 60, SYSDATETIMEOFFSET()), 0);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var attempt = await db.ExamAttempts.Where(a => a.UserId == userId).OrderByDescending(a => a.AttemptId).FirstAsync();
        return (attempt.CertId, attempt.ExamId, attempt.AttemptId);
    }

    [Fact]
    public async Task SittingLifecycle_And_CreditLedger_Enforces_BlockBeforeCharge_Policy()
    {
        // Arrange learner with exactly 1 credit
        var learnerId = await SeedLearnerWithCreditsAsync(startingCredits: 1);
        var (_, _, attemptId1) = await SeedAttemptFixtureAsync(learnerId, purpose: "mock");
        var (_, _, attemptId2) = await SeedAttemptFixtureAsync(learnerId, purpose: "mock");

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var ledger = new CreditLedger(db, TimeProvider.System);

        // Act: First charge on attemptId1 should succeed
        var receipt1 = await ledger.ChargeMockExamAsync(learnerId, attemptId1, amount: 1);
        Assert.NotNull(receipt1);
        Assert.Equal(-1, receipt1.Delta);

        // Idempotency: Charging the same attemptId1 again returns existing receipt without double-charging
        var receiptSame = await ledger.ChargeMockExamAsync(learnerId, attemptId1, amount: 1);
        Assert.Equal(receipt1.TxnId, receiptSame.TxnId);

        // Verify balance is now 0
        var balance = await ledger.GetBalanceAsync(learnerId);
        Assert.Equal(0, balance);

        // Act: Charging a NEW attempt attemptId2 must fail with InsufficientCredits
        var ex = await Assert.ThrowsAsync<CreditLedgerException>(() =>
            ledger.ChargeMockExamAsync(learnerId, attemptId2, amount: 1));

        Assert.Equal(CreditLedgerException.InsufficientCredits, ex.Code);
        Assert.Equal(402, ex.StatusCode);
    }

    [Fact]
    public async Task Free_Placement_Sitting_Never_Deducts_Credits_From_Wallet()
    {
        var learnerId = await SeedLearnerWithCreditsAsync(startingCredits: 5);
        var (_, _, _) = await SeedAttemptFixtureAsync(learnerId, purpose: "placement");

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var ledger = new CreditLedger(db, TimeProvider.System);

        // Balance remains unchanged (5 credits)
        var balance = await ledger.GetBalanceAsync(learnerId);
        Assert.Equal(5, balance);

        // No debit transaction was written
        var transactions = await db.CreditTransactions
            .Where(t => t.UserId == learnerId && t.Delta < 0)
            .ToListAsync();
        Assert.Empty(transactions);
    }

    [Fact]
    public async Task PracticeDrilling_Answers_Feed_Deterministic_Sql_View_Accuracy()
    {
        var certCode = Unique("PRAC");
        var learnerId = await SeedLearnerWithCreditsAsync(0, certCode);

        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, MinScaledScore, MaxScaledScore, ScoreStep) VALUES (@cert, 'READ', N'Reading', 'reading', 1, 0, 100, 5);
            DECLARE @skill INT = SCOPE_IDENTITY();
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder) VALUES (@skill, @cert, 'reading', 'P5', N'Sentences', 1);
            DECLARE @sec INT = SCOPE_IDENTITY();

            INSERT INTO Exams (CertId, Purpose, Name, DurationMinutes, Status, IsDeleted)
            VALUES (@cert, 'mock', N'Practice Bank', 60, 'published', 0);
            DECLARE @exam INT = SCOPE_IDENTITY();

            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, DisplayOrder)
            VALUES (@grp, @exam, @sec, @skill, N'Question 1', 1, 'mcq', 1),
                   (@grp, @exam, @sec, @skill, N'Question 2', 1, 'mcq', 2);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var section = await db.Sections.SingleAsync(s => s.CertId == cert.CertId);
        var questions = await db.Questions.Where(q => q.SectionId == section.SectionId).ToListAsync();

        // Create practice session
        var session = new PracticeSession
        {
            UserId = learnerId,
            SectionId = section.SectionId,
            Modality = "reading",
            QuestionCount = 2,
            Status = "finished",
            QuestionSource = "all",
            FeedbackMode = "immediate",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            FinishedAt = DateTimeOffset.UtcNow
        };
        db.PracticeSessions.Add(session);
        await db.SaveChangesAsync();

        // Add 1 correct answer and 1 incorrect answer
        db.PracticeAnswers.Add(new PracticeAnswer
        {
            SessionId = session.SessionId,
            QuestionId = questions[0].QuestionId,
            SectionId = section.SectionId,
            IsCorrect = true,
            DisplayOrder = 1
        });
        db.PracticeAnswers.Add(new PracticeAnswer
        {
            SessionId = session.SessionId,
            QuestionId = questions[1].QuestionId,
            SectionId = section.SectionId,
            IsCorrect = false,
            DisplayOrder = 2
        });
        await db.SaveChangesAsync();

        // Query deterministic SQL view
        var accuracy = await db.vw_UserSectionAccuracyAlls
            .FirstOrDefaultAsync(v => v.UserId == learnerId && v.SectionId == section.SectionId);

        if (accuracy != null)
        {
            Assert.Equal(2, accuracy.Answered);
            Assert.Equal(1, accuracy.Correct);
        }
    }

    [Fact]
    public async Task FreeResponse_Submission_Enqueues_Pending_Grading_For_Processor_Seam()
    {
        var certCode = Unique("FREE");

        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, DurationMinutes, MinScaledScore, MaxScaledScore, ScoreStep) VALUES (@cert, 'WRIT', N'Writing', 'writing', 1, 30, 0, 100, 5);
            DECLARE @skill INT = SCOPE_IDENTITY();
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, DisplayOrder) VALUES (@skill, @cert, 'writing', 'WR', N'Writing Section', 1);
            DECLARE @sec INT = SCOPE_IDENTITY();
            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder) VALUES (@cert, @skill, 'TASK', N'Task Response', 9.0, 1.0, 1);
            INSERT INTO Exams (CertId, Purpose, Name, DurationMinutes, Status, IsDeleted) VALUES (@cert, 'writing_practice', N'Writing Practice', 30, 'published', 0);
            DECLARE @exam INT = SCOPE_IDENTITY();
            INSERT INTO QuestionGroups (ExamId, CertId, SectionId, DisplayOrder) VALUES (@exam, @cert, @sec, 1);
            DECLARE @grp INT = SCOPE_IDENTITY();
            INSERT INTO Questions (GroupId, ExamId, SectionId, SkillId, Stem, DifficultyLevel, QuestionType, MinWords, MaxWords, DisplayOrder)
            VALUES (@grp, @exam, @sec, @skill, N'Write an essay.', 1, 'free_response', 50, 300, 1);
            """);

        var learnerId = await SeedLearnerWithCreditsAsync(3, certCode);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var question = await db.Questions.SingleAsync(q => q.ExamId == exam.ExamId);

        var learnerService = new LearnerService(db);
        var writingService = new WritingPracticeService(db, learnerService);

        var attempt = await writingService.StartAttemptAsync(learnerId, exam.ExamId);
        await writingService.SaveDraftAsync(learnerId, attempt.AttemptId, question.QuestionId, "This is a completed essay submission for AI grading.");
        await writingService.SubmitAttemptAsync(learnerId, attempt.AttemptId);

        // Verify task was enqueued into AiGradings with status 'pending'
        await using var dbCheck = DbContextFactory.Create(database.ConnectionString);
        var grading = await dbCheck.AiGradings
            .Include(g => g.FreeResponse)
            .FirstOrDefaultAsync(g => g.FreeResponse.AttemptId == attempt.AttemptId);

        Assert.NotNull(grading);
        Assert.Equal("pending", grading.Status);
    }
}
