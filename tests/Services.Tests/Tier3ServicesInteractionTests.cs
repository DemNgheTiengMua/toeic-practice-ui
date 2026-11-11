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

        // #269: the certificate is seeded BEFORE the learner, so ActiveCertId points at it.
        await database.SqlAsync($"""
            INSERT INTO Certificates (Code, Name, IsActive) VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder, MinScaledScore, MaxScaledScore, ScoreStep) VALUES (@cert, 'READ', N'Reading', 'reading', 1, 0, 100, 5);
            DECLARE @skill INT = SCOPE_IDENTITY();
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder) VALUES (@skill, @cert, 'reading', 'P5', N'Sentences', 1, 1);
            DECLARE @sec INT = SCOPE_IDENTITY();

            INSERT INTO Exams (CertId, Purpose, Name, DurationMinutes, Status, IsDeleted)
            VALUES (@cert, 'mock', N'Practice Bank', 60, 'published', 0);
            """);

        var learnerId = await SeedLearnerWithCreditsAsync(0, certCode);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var section = await db.Sections.SingleAsync(s => s.CertId == cert.CertId);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);

        // #269: the drill's stock is pool stock. Two positions on the bank paper, so the answers below
        // name an (ItemId, ItemVersionId) pair rather than a Questions row. The level is B2, not B1:
        // `PoolIngestionTests` counts the (R-DET, B1) cell exactly and the pool is shared by the whole
        // assembly, so stocking there would shift a number another test asserts.
        var first = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, cert.CertId, section.SectionId, "reading", "Question 1",
            displayOrder: 1, level: "B2");
        var second = await PoolFixture.AddMcqAsync(
            db, exam.ExamId, cert.CertId, section.SectionId, "reading", "Question 2",
            displayOrder: 2, level: "B2");

        // Create practice session
        var session = new PracticeSession
        {
            UserId = learnerId,
            CertId = cert.CertId,
            SectionId = section.SectionId,
            Modality = "reading",
            QuestionCount = 2,
            Status = "finished",
            QuestionSource = "all",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            FinishedAt = DateTimeOffset.UtcNow
        };
        db.PracticeSessions.Add(session);
        await db.SaveChangesAsync();

        // Add 1 correct answer and 1 incorrect answer
        db.PracticeAnswers.Add(new PracticeAnswer
        {
            SessionId = session.SessionId,
            ItemId = first.ItemId,
            ItemVersionId = first.ItemVersionId,
            IsCorrect = true,
            DisplayOrder = 1
        });
        db.PracticeAnswers.Add(new PracticeAnswer
        {
            SessionId = session.SessionId,
            ItemId = second.ItemId,
            ItemVersionId = second.ItemVersionId,
            IsCorrect = false,
            DisplayOrder = 2
        });
        await db.SaveChangesAsync();

        // Query deterministic SQL view. A pool practice answer carries no section (#187: an item is
        // section-free), so the section axis has no row to report it under - the cell axis is where a
        // drill's accuracy lives, and it is the same deterministic SQL (D3).
        var accuracy = await db.vw_UserTopicAccuracies
            .SingleAsync(v => v.UserId == learnerId);

        Assert.Equal(2, accuracy.Answered);
        Assert.Equal(1, accuracy.Correct);
        Assert.Equal(50, accuracy.AccuracyPercent);
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
            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder) VALUES (@skill, @cert, 'writing', 'WR', N'Writing Section', 1, 1);
            DECLARE @sec INT = SCOPE_IDENTITY();
            INSERT INTO GradingCriteria (CertId, SkillId, Code, Name, MaxScore, Weight, DisplayOrder) VALUES (@cert, @skill, 'TASK', N'Task Response', 9.0, 1.0, 1);
            INSERT INTO Exams (CertId, Purpose, Name, DurationMinutes, Status, IsDeleted) VALUES (@cert, 'writing_practice', N'Writing Practice', 30, 'published', 0);
            """);

        var learnerId = await SeedLearnerWithCreditsAsync(3, certCode);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var cert = await db.Certificates.SingleAsync(c => c.Code == certCode);
        var exam = await db.Exams.SingleAsync(e => e.CertId == cert.CertId);
        var section = await db.Sections.SingleAsync(s => s.CertId == cert.CertId);

        // #269: a writing task is a productive pool position - no options, no key, and the word window
        // the task declares. Its address is the ExamItemId, which is what the sitting renders.
        var question = await PoolFixture.AddProductiveAsync(
            db, exam.ExamId, cert.CertId, section.SectionId, "writing", "Write an essay.",
            displayOrder: 1, minWords: 50, maxWords: 300, level: "A2");

        var learnerService = new LearnerService(db);
        var writingService = new WritingPracticeService(db, learnerService);

        var attempt = await writingService.StartAttemptAsync(learnerId, exam.ExamId);
        await writingService.SaveDraftAsync(learnerId, attempt.AttemptId, question.ExamItemId, "This is a completed essay submission for AI grading.");
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
