using System;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #280: <b>the schema and the model agree about a refund</b>, proven against a real database whose
/// triggers and check constraints are present.
/// <para>
/// The schema harness proves the constraints; it cannot prove EF can save a row. A table with an SQL
/// trigger and no <c>HasTrigger</c> declaration fails on the <c>OUTPUT</c> clause at runtime, in the
/// one place no schema test looks (ADR-0001, and <see cref="HasTriggerDriftTests"/> names the two
/// triggers that shipped this way). So this file drives the real entity through the real triggers.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RefundLedgerSchemaTests(DatabaseFixture database)
{
    [Fact]
    public async Task EF_can_save_a_refund_and_the_append_only_trigger_survives_the_declaration()
    {
        var learner = await SeedLearnerAsync();
        var admin = await SeedAdminAsync();

        await using var db = DbContextFactory.Create(database.ConnectionString);

        // A money refund needs no ledger row, so this exercises HasTrigger with no other moving part.
        var refund = new Refund
        {
            UserId = learner,
            Method = "money",
            Reason = "duplicate charge",
            Amount = 100_000.00m,
            RequestedByUserId = admin,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Refunds.Add(refund);
        await db.SaveChangesAsync();

        Assert.True(refund.RefundId > 0, "the inserted refund did not come back with its identity, which is "
            + "the OUTPUT-clause failure HasTrigger prevents");

        // The default the schema declares, read back from the database rather than assumed.
        var stored = await db.Refunds.AsNoTracking().SingleAsync(r => r.RefundId == refund.RefundId);
        Assert.Equal("requested", stored.Status);
        Assert.Equal(100_000.00m, stored.Amount);
        Assert.Null(stored.CreditCount);
    }

    [Fact]
    public async Task A_credit_refund_posts_a_refund_credit_row_that_the_balance_counts()
    {
        var learner = await SeedLearnerAsync();
        var admin = await SeedAdminAsync();

        await using var db = DbContextFactory.Create(database.ConnectionString);

        // The ledger row a credit refund posts: positive, with the deciding admin and a note.
        var txn = new CreditTransaction
        {
            UserId = learner,
            Reason = CreditReason.RefundCredit.ToCode(),
            Delta = 25,
            AdminUserId = admin,
            Note = "refund for a failed booking",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.CreditTransactions.Add(txn);
        await db.SaveChangesAsync();

        var refund = new Refund
        {
            UserId = learner,
            Method = "credit",
            Reason = "failed booking",
            Status = "approved",
            CreditCount = 25,
            RequestedByUserId = admin,
            DecidedByUserId = admin,
            DecidedAt = DateTimeOffset.UtcNow,
            CreditTxnId = txn.TxnId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Refunds.Add(refund);
        await db.SaveChangesAsync();

        // vw_UserCreditBalance is a plain SUM over the ledger, so a refund_credit raises the balance
        // with no view change. That is the learner-visible consequence of the new reason.
        var balance = await db.vw_UserCreditBalances
            .Where(b => b.UserId == learner)
            .Select(b => b.Balance)
            .SingleAsync();

        Assert.Equal(25, balance);

        // And the refund names that exact row, so "which ledger row paid this refund" is answerable.
        Assert.Equal(txn.TxnId, refund.CreditTxnId);
    }

    /// <summary>
    /// The sign rule is the one a service is most likely to get wrong, because a "claw back" reads like
    /// a refund too. The database refuses it: <c>refund_credit</c> is positive by definition.
    /// </summary>
    [Fact]
    public async Task The_ledger_refuses_a_negative_refund_credit()
    {
        var learner = await SeedLearnerAsync();
        var admin = await SeedAdminAsync();

        await using var db = DbContextFactory.Create(database.ConnectionString);
        db.CreditTransactions.Add(new CreditTransaction
        {
            UserId = learner,
            Reason = CreditReason.RefundCredit.ToCode(),
            Delta = -25,
            AdminUserId = admin,
            Note = "clawback",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("CK_CreditTxn_DeltaSign", error.InnerException?.Message ?? error.Message);
    }

    private async Task<int> SeedLearnerAsync()
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), '{email}', 0x01, N'Refund Learner', SYSDATETIMEOFFSET());
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }

    private async Task<int> SeedAdminAsync()
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        await database.SqlAsync($"""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'admin')
              INSERT INTO Roles (Code, Name) VALUES ('admin', N'Admin');
            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, EmailVerifiedAt)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'admin'), '{email}', 0x01, N'Refund Admin', SYSDATETIMEOFFSET());
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }
}
