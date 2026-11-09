using BusinessObjects;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Services.Credits;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #247.2: <b>every ledger balance after the cutover equals the balance in the pre-cutover snapshot, and
/// a discrepancy blocks the step.</b>
/// <para>
/// <b>Why this check and not "the balance equals the ledger sum".</b> The imported database is internally
/// consistent by construction - the balance IS the sum of the rows it holds - so a database missing rows
/// still reports a balance that agrees with itself. Only a comparison against a number captured BEFORE
/// the window can see the loss. Three cases below create three different kinds of loss and require the
/// service to catch each one.
/// </para>
/// <para>
/// <b>Which guard could this pass while being wrong.</b> "Reconcile returns true on an unchanged
/// database" passes on a service that always returns true. Each failure case therefore creates the
/// discrepancy first and asserts the KIND, and the last case proves a clean import still reconciles -
/// without it a service that always failed would pass the three above.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ReconciliationTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    /// <summary>An unchanged database reconciles against its own snapshot. The control case.</summary>
    [Fact]
    public async Task A_database_that_did_not_move_reconciles_against_its_snapshot()
    {
        var name = $"EERec0_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var userId = await SeedAsync(connectionString, 5, -2);

            await using (var db = DbContextFactory.Create(connectionString))
            {
                var service = new ReconciliationService(db);
                var snapshot = await service.SnapshotAsync();
                var result = await service.ReconcileAsync(snapshot);

                Assert.True(result.Reconciled, result.BlockingReason);
                Assert.Null(result.BlockingReason);
                Assert.Equal(1, result.UsersChecked);
                Assert.Contains(snapshot.Rows, r => r.UserId == userId && r.Balance == 3 && r.TxnCount == 2);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>A wallet that came back with LESS than it left is the failure the ticket names.</summary>
    [Fact]
    public async Task A_wallet_that_came_back_short_is_reported_and_blocks_the_step()
    {
        var name = $"EERec1_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var userId = await SeedAsync(connectionString, 10, -4);

            BalanceSnapshot snapshot;
            await using (var db = DbContextFactory.Create(connectionString))
            {
                snapshot = await new ReconciliationService(db).SnapshotAsync();
            }

            // Simulate the cutover dropping one row, as an incomplete import would.
            await using (var db = DbContextFactory.Create(connectionString))
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CreditTransactions DISABLE TRIGGER ALL");
                await db.Database.ExecuteSqlRawAsync(
                    $"DELETE FROM CreditTransactions WHERE TxnId = (SELECT MIN(TxnId) FROM CreditTransactions WHERE UserId = {userId})");
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CreditTransactions ENABLE TRIGGER ALL");
            }

            await using (var db = DbContextFactory.Create(connectionString))
            {
                var result = await new ReconciliationService(db).ReconcileAsync(snapshot);

                Assert.False(result.Reconciled);
                var discrepancy = Assert.Single(result.Discrepancies);
                Assert.Equal(userId, discrepancy.UserId);
                Assert.Equal("balance", discrepancy.Kind);
                // The MIN(TxnId) row is the +10 grant, so deleting it leaves only the -4 revoke: the
                // wallet came back 4 short of zero rather than 6 short, which is still a discrepancy and
                // still blocks. Asserted exactly so the test states which row it removed.
                Assert.Equal(6, discrepancy.ExpectedBalance);
                Assert.Equal(-4, discrepancy.ActualBalance);
                Assert.NotNull(result.BlockingReason);
                Assert.Contains("must not proceed", result.BlockingReason!, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// The same total reached by a different history: two rows replaced by one that nets the same.
    /// A balance check alone cannot see this, which is why the row count travels with the sum.
    /// </summary>
    [Fact]
    public async Task The_same_balance_from_a_different_row_count_is_reported_as_a_row_count_defect()
    {
        var name = $"EERec2_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var userId = await SeedAsync(connectionString, 10, -4);

            BalanceSnapshot snapshot;
            await using (var db = DbContextFactory.Create(connectionString))
            {
                snapshot = await new ReconciliationService(db).SnapshotAsync();
            }

            // Two rows become one that nets the same 6. The balance is right; the history is not.
            await using (var db = DbContextFactory.Create(connectionString))
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CreditTransactions DISABLE TRIGGER ALL");
                await db.Database.ExecuteSqlRawAsync($"DELETE FROM CreditTransactions WHERE UserId = {userId}");
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CreditTransactions ENABLE TRIGGER ALL");
            }

            await using (var db = DbContextFactory.Create(connectionString))
            {
                db.CreditTransactions.Add(new BusinessObjects.Entities.CreditTransaction
                {
                    UserId = userId,
                    Reason = "purchase",
                    Delta = 6,
                    OrderId = await SeedPaidOrderAsync(db, userId),
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
            }

            await using (var db = DbContextFactory.Create(connectionString))
            {
                var result = await new ReconciliationService(db).ReconcileAsync(snapshot);

                Assert.False(result.Reconciled);
                var discrepancy = Assert.Single(result.Discrepancies);
                Assert.Equal("row_count", discrepancy.Kind);
                Assert.Equal(discrepancy.ExpectedBalance, discrepancy.ActualBalance);
                Assert.NotEqual(discrepancy.ExpectedTxnCount, discrepancy.ActualTxnCount);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// A user the snapshot never had, carrying credits. This is a NEW wallet, not a preserved one, and a
    /// cutover that invented one has not preserved anything - it has added money.
    /// </summary>
    [Fact]
    public async Task A_wallet_the_snapshot_never_had_is_reported_as_unexpected()
    {
        var name = $"EERec3_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            await SeedAsync(connectionString, 4, -1);

            BalanceSnapshot snapshot;
            await using (var db = DbContextFactory.Create(connectionString))
            {
                snapshot = await new ReconciliationService(db).SnapshotAsync();
            }

            var stranger = await SeedAsync(connectionString, 100, 0);

            await using (var db = DbContextFactory.Create(connectionString))
            {
                var result = await new ReconciliationService(db).ReconcileAsync(snapshot);

                Assert.False(result.Reconciled);
                var discrepancy = Assert.Single(result.Discrepancies);
                Assert.Equal(stranger, discrepancy.UserId);
                Assert.Equal("unexpected_wallet", discrepancy.Kind);
                Assert.Equal(0, discrepancy.ExpectedBalance);
                Assert.Equal(100, discrepancy.ActualBalance);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ---- fixtures ---------------------------------------------------------------------------

    /// <summary>A learner with exactly two ledger rows: one grant and one revoke.</summary>
    private static async Task<int> SeedAsync(string connectionString, int grant, int revoke)
    {
        await using var db = DbContextFactory.Create(connectionString);

        var learnerRole = new BusinessObjects.Entities.Role { Code = Unique("lrn"), Name = "Learner" };
        var adminRole = new BusinessObjects.Entities.Role { Code = Unique("adm"), Name = "Admin" };
        db.Roles.AddRange(learnerRole, adminRole);
        await db.SaveChangesAsync();

        var learner = new BusinessObjects.Entities.User
        {
            RoleId = learnerRole.RoleId,
            Email = $"{Guid.NewGuid():N}@rec.test",
            PasswordHash = [1],
            FullName = "Reconciliation learner",
        };
        var admin = new BusinessObjects.Entities.User
        {
            RoleId = adminRole.RoleId,
            Email = $"{Guid.NewGuid():N}@rec-admin.test",
            PasswordHash = [1],
            FullName = "Reconciliation admin",
        };
        db.Users.AddRange(learner, admin);
        await db.SaveChangesAsync();

        db.CreditTransactions.Add(new BusinessObjects.Entities.CreditTransaction
        {
            UserId = learner.UserId,
            Reason = "admin_grant",
            Delta = grant,
            AdminUserId = admin.UserId,
            Note = "reconciliation fixture grant",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
        });

        if (revoke != 0)
        {
            db.CreditTransactions.Add(new BusinessObjects.Entities.CreditTransaction
            {
                UserId = learner.UserId,
                Reason = "admin_revoke",
                Delta = revoke,
                AdminUserId = admin.UserId,
                Note = "reconciliation fixture revoke",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
        }

        await db.SaveChangesAsync();
        return learner.UserId;
    }

    /// <summary>A paid order, because CK_CreditTxn_Source requires a purchase to name one.</summary>
    private static async Task<int> SeedPaidOrderAsync(CompanionDbContext db, int userId)
    {
        var package = new BusinessObjects.Entities.Package
        {
            Name = $"Rec package {Guid.NewGuid():N}"[..30],
            Price = 100_000,
            Credits = 6,
            IsActive = true,
        };
        db.Packages.Add(package);
        await db.SaveChangesAsync();

        var order = new BusinessObjects.Entities.Order
        {
            UserId = userId,
            PackageId = package.PackageId,
            OrderCode = $"REC{Guid.NewGuid():N}"[..16],
            Status = "paid",
            Amount = 100_000,
            Gateway = "mock",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.OrderId;
    }
}
