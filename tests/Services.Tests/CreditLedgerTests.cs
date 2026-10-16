using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;
using Services.Credits;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class CreditLedgerTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private CreditLedger CreateLedger(CompanionDbContext db) => new(db, TimeProvider.System);

    private async Task<int> SeedLearnerAsync(int startingCredits = 0)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Test Learner', SYSDATETIMEOFFSET());
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
            SELECT @cert AS CertId, @exam AS ExamId, SCOPE_IDENTITY() AS AttemptId;
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var attempt = await db.ExamAttempts.Where(a => a.UserId == userId).OrderByDescending(a => a.AttemptId).FirstAsync();
        return (attempt.CertId, attempt.ExamId, attempt.AttemptId);
    }

    [Fact]
    public async Task GetBalanceAsync_WhenNoTransactions_ReturnsZero()
    {
        var userId = await SeedLearnerAsync(startingCredits: 0);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var ledger = CreateLedger(db);

        var balance = await ledger.GetBalanceAsync(userId);
        Assert.Equal(0, balance);
    }

    [Fact]
    public async Task GetBalanceAsync_WithInitialCredits_ReturnsBalance()
    {
        var userId = await SeedLearnerAsync(startingCredits: 10);
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var ledger = CreateLedger(db);

        var balance = await ledger.GetBalanceAsync(userId);
        Assert.Equal(10, balance);
    }

    [Fact]
    public async Task ChargeMockExamAsync_WhenInsufficientCredits_ThrowsInsufficientCredits()
    {
        var userId = await SeedLearnerAsync(startingCredits: 0);
        var (_, _, attemptId) = await SeedAttemptFixtureAsync(userId, purpose: "mock");

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var ledger = CreateLedger(db);

        var ex = await Assert.ThrowsAsync<CreditLedgerException>(() =>
            ledger.ChargeMockExamAsync(userId, attemptId, amount: 1));

        Assert.Equal(CreditLedgerException.InsufficientCredits, ex.Code);
        Assert.Equal(402, ex.StatusCode);
    }

    [Fact]
    public async Task ChargeMockExamAsync_WhenSufficientCredits_DeductsAndReturnsReceipt()
    {
        var userId = await SeedLearnerAsync(startingCredits: 5);
        var (_, _, attemptId) = await SeedAttemptFixtureAsync(userId, purpose: "mock");

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var ledger = CreateLedger(db);

        var receipt = await ledger.ChargeMockExamAsync(userId, attemptId, amount: 1);

        Assert.True(receipt.TxnId > 0);
        Assert.Equal(userId, receipt.UserId);
        Assert.Equal(CreditReason.MockExamStart, receipt.Reason);
        Assert.Equal(-1, receipt.Delta);
        Assert.Equal(4, receipt.BalanceAfter);

        var newBalance = await ledger.GetBalanceAsync(userId);
        Assert.Equal(4, newBalance);
    }

    [Fact]
    public async Task ChargeMockExamAsync_WhenCalledTwice_IsIdempotent()
    {
        var userId = await SeedLearnerAsync(startingCredits: 5);
        var (_, _, attemptId) = await SeedAttemptFixtureAsync(userId, purpose: "mock");

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var ledger = CreateLedger(db);

        var firstReceipt = await ledger.ChargeMockExamAsync(userId, attemptId, amount: 1);
        var secondReceipt = await ledger.ChargeMockExamAsync(userId, attemptId, amount: 1);

        Assert.Equal(firstReceipt.TxnId, secondReceipt.TxnId);
        Assert.Equal(firstReceipt.Delta, secondReceipt.Delta);
        Assert.Equal(4, secondReceipt.BalanceAfter);

        var balance = await ledger.GetBalanceAsync(userId);
        Assert.Equal(4, balance); // not deducted twice!
    }

    [Fact]
    public async Task ChargeMockExamAsync_WhenAttemptOwnedByOtherUser_ThrowsTargetOwnerMismatch()
    {
        var user1 = await SeedLearnerAsync(startingCredits: 5);
        var user2 = await SeedLearnerAsync(startingCredits: 5);
        var (_, _, attemptId) = await SeedAttemptFixtureAsync(user1, purpose: "mock");

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var ledger = CreateLedger(db);

        var ex = await Assert.ThrowsAsync<CreditLedgerException>(() =>
            ledger.ChargeMockExamAsync(user2, attemptId, amount: 1));

        Assert.Equal(CreditLedgerException.TargetOwnerMismatch, ex.Code);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public async Task AdjustCreditsAsync_GrantAndRevoke_EnforcesInvariants()
    {
        var userId = await SeedLearnerAsync(startingCredits: 2);
        var adminId = await SeedLearnerAsync(startingCredits: 0);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var ledger = CreateLedger(db);

        // Grant 3 credits
        var grantReceipt = await ledger.AdjustCreditsAsync(userId, adminId, delta: 3, reason: "Bonus credits");
        Assert.Equal(5, grantReceipt.BalanceAfter);
        Assert.Equal(CreditReason.AdminGrant, grantReceipt.Reason);

        // Revoke 2 credits
        var revokeReceipt = await ledger.AdjustCreditsAsync(userId, adminId, delta: -2, reason: "Correction");
        Assert.Equal(3, revokeReceipt.BalanceAfter);
        Assert.Equal(CreditReason.AdminRevoke, revokeReceipt.Reason);

        // Revoke exceeding balance throws InsufficientCredits
        var ex = await Assert.ThrowsAsync<CreditLedgerException>(() =>
            ledger.AdjustCreditsAsync(userId, adminId, delta: -10, reason: "Excessive revoke"));
        Assert.Equal(CreditLedgerException.InsufficientCredits, ex.Code);
    }

    [Fact]
    public async Task GetLedgerHistoryAsync_ReturnsOrderedEntries()
    {
        var userId = await SeedLearnerAsync(startingCredits: 5);
        var adminId = await SeedLearnerAsync(startingCredits: 0);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var ledger = CreateLedger(db);

        await ledger.AdjustCreditsAsync(userId, adminId, delta: 2, reason: "Adjustment 1");
        await ledger.AdjustCreditsAsync(userId, adminId, delta: 1, reason: "Adjustment 2");

        var history = await ledger.GetLedgerHistoryAsync(userId);
        Assert.True(history.Count >= 3);
        // Ordered by CreatedAt DESC
        Assert.True(history[0].CreatedAt >= history[1].CreatedAt);
    }
}
