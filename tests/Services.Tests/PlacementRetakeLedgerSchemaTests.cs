using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services.Credits;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #288: <b>the schema, the EF model, and the balance view agree on <c>placement_retake</c></b>,
/// proven against a real database whose triggers and check constraints are active.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PlacementRetakeLedgerSchemaTests(DatabaseFixture database)
{
    [Fact]
    public async Task EF_can_save_a_placement_retake_and_balance_view_equals_ledger_sum()
    {
        var learner = await SeedUserAsync("learner");
        var admin = await SeedUserAsync("admin");

        await using var db = DbContextFactory.Create(database.ConnectionString);

        db.CreditTransactions.Add(new CreditTransaction
        {
            UserId = learner,
            Reason = CreditReason.AdminGrant.ToCode(),
            Delta = 10,
            AdminUserId = admin,
            Note = "Seed credits for placement retake test",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var retakeTxn = new CreditTransaction
        {
            UserId = learner,
            Reason = CreditReason.PlacementRetake.ToCode(),
            Delta = -5,
            Note = "Placement retake: all 4 skills",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.CreditTransactions.Add(retakeTxn);
        await db.SaveChangesAsync();

        Assert.True(retakeTxn.TxnId > 0, "inserted placement_retake did not return an identity (HasTrigger/OUTPUT check)");

        var viewBalance = await db.vw_UserCreditBalances
            .Where(b => b.UserId == learner)
            .Select(b => b.Balance)
            .SingleAsync();
        var ledgerSum = await db.Database
            .SqlQuery<int>($"SELECT ISNULL(SUM(Delta), 0) AS Value FROM CreditTransactions WHERE UserId = {learner}")
            .SingleAsync();
        var serviceBalance = await new CreditLedger(db).GetBalanceAsync(learner);

        Assert.Equal(5, ledgerSum);
        Assert.Equal(5, viewBalance);
        Assert.Equal(5, serviceBalance);

        var history = await new CreditLedger(db).GetLedgerHistoryAsync(learner);
        var retakeEntry = Assert.Single(history, h => h.Reason == CreditReasonDto.PlacementRetake);
        Assert.Equal(-5, retakeEntry.Delta);
        Assert.Equal("Placement retake: all 4 skills", retakeEntry.Note);
    }

    [Fact]
    public async Task The_ledger_refuses_a_positive_placement_retake()
    {
        var learner = await SeedUserAsync("learner");

        await using var db = DbContextFactory.Create(database.ConnectionString);
        db.CreditTransactions.Add(new CreditTransaction
        {
            UserId = learner,
            Reason = CreditReason.PlacementRetake.ToCode(),
            Delta = 5,
            Note = "Invalid positive placement retake",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("CK_CreditTxn_DeltaSign", error.InnerException?.Message ?? error.Message);
    }

    [Fact]
    public async Task The_ledger_refuses_a_placement_retake_naming_an_admin_user()
    {
        var learner = await SeedUserAsync("learner");
        var admin = await SeedUserAsync("admin");
        await GrantCreditsAsync(learner, admin, 10);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        db.CreditTransactions.Add(new CreditTransaction
        {
            UserId = learner,
            Reason = CreditReason.PlacementRetake.ToCode(),
            Delta = -3,
            AdminUserId = admin,
            Note = "Retake must not carry an admin actor",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("CK_CreditTxn_AdminActor", error.InnerException?.Message ?? error.Message);
    }

    [Fact]
    public async Task The_ledger_refuses_a_placement_retake_without_a_note()
    {
        var learner = await SeedUserAsync("learner");
        var admin = await SeedUserAsync("admin");
        await GrantCreditsAsync(learner, admin, 10);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        db.CreditTransactions.Add(new CreditTransaction
        {
            UserId = learner,
            Reason = CreditReason.PlacementRetake.ToCode(),
            Delta = -3,
            Note = null,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("CK_CreditTxn_AdminActor", error.InnerException?.Message ?? error.Message);
    }

    [Theory]
    [InlineData("OrderId")]
    [InlineData("AttemptId")]
    [InlineData("BookingId")]
    public async Task The_ledger_refuses_a_placement_retake_naming_a_source_row(string sourceColumn)
    {
        var learner = await SeedUserAsync("learner");
        var admin = await SeedUserAsync("admin");
        await GrantCreditsAsync(learner, admin, 10);
        var (orderId, attemptId, bookingId) = await SeedSourceRowsAsync(learner);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        db.CreditTransactions.Add(new CreditTransaction
        {
            UserId = learner,
            Reason = CreditReason.PlacementRetake.ToCode(),
            Delta = -3,
            OrderId = sourceColumn == "OrderId" ? orderId : null,
            AttemptId = sourceColumn == "AttemptId" ? attemptId : null,
            BookingId = sourceColumn == "BookingId" ? bookingId : null,
            Note = "Retake must not carry a source row",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("CK_CreditTxn_Source", error.InnerException?.Message ?? error.Message);
    }

    private async Task<int> SeedUserAsync(string roleCode)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = '{roleCode}')
              INSERT INTO Roles (Code, Name) VALUES ('{roleCode}', N'{roleCode}');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = '{roleCode}'), '{email}', 0x01, N'Placement Retake Test User', SYSDATETIMEOFFSET());
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    private async Task GrantCreditsAsync(int learnerId, int adminId, int amount)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        db.CreditTransactions.Add(new CreditTransaction
        {
            UserId = learnerId,
            Reason = CreditReason.AdminGrant.ToCode(),
            Delta = amount,
            AdminUserId = adminId,
            Note = "Seed credits",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<(int orderId, int attemptId, int bookingId)> SeedSourceRowsAsync(int learnerId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var orderCode = $"DH-PR-{suffix}";
        var certCode = $"PR_{suffix}";
        await database.SqlAsync($"""
            INSERT INTO Packages (Name, Credits, Price, IsActive)
            VALUES (N'PR Package {suffix}', 10, 100000, 1);
            DECLARE @pk INT = SCOPE_IDENTITY();
            INSERT INTO Orders (UserId, PackageId, Status, Amount, OrderCode, PaymentNo, Gateway)
            VALUES ({learnerId}, @pk, 'paid', 100000, '{orderCode}', NEXT VALUE FOR SEQ_PaymentNo, 'mock');

            INSERT INTO Certificates (Code, Name, IsActive)
            VALUES ('{certCode}', N'Cert {certCode}', 1);
            DECLARE @cert INT = SCOPE_IDENTITY();
            INSERT INTO Exams (CertId, Purpose, Name, DurationMinutes, Status, IsDeleted)
            VALUES (@cert, 'placement', N'Placement {suffix}', 45, 'published', 0);
            DECLARE @exam INT = SCOPE_IDENTITY();
            INSERT INTO ExamAttempts (UserId, ExamId, CertId, Status, StartedAt, ExpiresAt, IsAutoSubmitted)
            VALUES ({learnerId}, @exam, @cert, 'in_progress', SYSDATETIMEOFFSET(), DATEADD(hour, 72, SYSDATETIMEOFFSET()), 0);

            INSERT INTO MentorProfiles (DisplayName, CertId, Type, PricePerSlot, IsActive)
            VALUES (N'Coach {suffix}', @cert, 'ai', 0, 1);
            DECLARE @mentor INT = SCOPE_IDENTITY();
            INSERT INTO Bookings (UserId, MentorId, MentorType, Status)
            VALUES ({learnerId}, @mentor, 'ai', 'done');
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var orderId = await db.Orders.Where(o => o.OrderCode == orderCode).Select(o => o.OrderId).SingleAsync();
        var attemptId = await db.ExamAttempts.Where(a => a.UserId == learnerId).OrderByDescending(a => a.AttemptId).Select(a => a.AttemptId).FirstAsync();
        var bookingId = await db.Bookings.Where(b => b.UserId == learnerId).OrderByDescending(b => b.BookingId).Select(b => b.BookingId).FirstAsync();
        return (orderId, attemptId, bookingId);
    }
}
