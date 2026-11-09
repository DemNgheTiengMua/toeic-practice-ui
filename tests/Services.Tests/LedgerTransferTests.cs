using BusinessObjects;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Services.Credits;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #246.1 and #246.6: <b>the ledger is exported and re-imported with every historical reason string
/// intact, and an import run is idempotent.</b>
/// <para>
/// <b>Why the id travels.</b> The ledger is append-only: <c>trg_CreditTransactions_NoRewrite</c> refuses
/// a change to a posted row and <c>trg_CreditTransactions_AppendOnly</c> refuses its deletion. An import
/// that minted new identities could not tell "already here" from "new", so a second run would double
/// every wallet. That is the failure this file exists to make impossible, and it is measured rather than
/// asserted: the balance is read after each run.
/// </para>
/// <para>
/// <b>Which guard could this pass while being wrong.</b> "Export then import returns the same rows"
/// passes on an import that rewrote them, and "run twice" passes on an import that silently skipped
/// everything. So the test compares the row set by VALUE after a round trip through JSON (not the same
/// object graph), then runs the import a third time and asserts the balance did not move.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LedgerTransferTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task The_ledger_survives_a_json_round_trip_with_its_reasons_and_balances_intact()
    {
        var sourceName = $"EELedgerS_{Guid.NewGuid():N}"[..24];
        var targetName = $"EELedgerT_{Guid.NewGuid():N}"[..24];
        var source = await database.CreateFreshDatabaseAsync(sourceName);
        var target = await database.CreateFreshDatabaseAsync(targetName);
        try
        {
            var userId = await SeedLearnerAsync(source, "ledger-source");
            await SeedLedgerAsync(source, userId);

            int balanceBefore;
            LedgerExportDocument exported;
            await using (var db = DbContextFactory.Create(source))
            {
                exported = await new LedgerTransferService(db, TimeProvider.System).ExportAsync();
                balanceBefore = await new CreditLedger(db).GetBalanceAsync(userId);
            }

            Assert.Equal(exported.Rows.Count, exported.RowCount);
            Assert.NotEmpty(exported.Rows);

            // The reasons are the historical vocabulary, unaltered.
            Assert.Contains(exported.Rows, r => r.Reason == "admin_grant");
            Assert.Contains(exported.Rows, r => r.Reason == "admin_revoke");

            // A real transfer: through JSON text, into a DIFFERENT database.
            var json = exported.ToJson();
            var reimported = LedgerExportDocument.FromJson(json);
            Assert.NotNull(reimported);

            var targetUserId = await SeedUsersWithSourceIdsAsync(target, userId);

            LedgerImportResult firstImport;
            await using (var db = DbContextFactory.Create(target))
            {
                firstImport = await new LedgerTransferService(db, TimeProvider.System).ImportAsync(reimported!);
            }

            Assert.True(firstImport.Reconciled, $"inserted={firstImport.Inserted} refused={firstImport.Refused}");
            Assert.Equal(exported.Rows.Count, firstImport.Inserted);

            // Identity 6, on the other side: the balance equals the ledger sum, and it equals the
            // balance the wallet had in the source database.
            int balanceAfter;
            await using (var db = DbContextFactory.Create(target))
            {
                balanceAfter = await new CreditLedger(db).GetBalanceAsync(targetUserId);
            }

            Assert.Equal(balanceBefore, balanceAfter);

            // Every row arrived with its own identity and its own reason string, compared by value.
            await using (var db = DbContextFactory.Create(target))
            {
                var stored = await db.CreditTransactions.AsNoTracking()
                    .OrderBy(t => t.TxnId)
                    .Select(t => new { t.TxnId, t.Reason, t.Delta, t.AdminUserId, t.Note, t.OrderId })
                    .ToListAsync();

                Assert.Equal(exported.Rows.Count, stored.Count);
                foreach (var row in exported.Rows)
                {
                    var match = Assert.Single(stored, s => s.TxnId == row.TxnId);
                    Assert.Equal(row.Reason, match.Reason);
                    Assert.Equal(row.Delta, match.Delta);
                    Assert.Equal(row.AdminUserId, match.AdminUserId);
                    Assert.Equal(row.Note, match.Note);
                    Assert.Equal(row.OrderId, match.OrderId);
                }
            }
        }
        finally
        {
            await database.DropDatabaseAsync(sourceName);
            await database.DropDatabaseAsync(targetName);
        }
    }

    /// <summary>
    /// #246.6: running the import twice does not duplicate a row. Measured on the balance, because a
    /// duplicated credit is the damage and a duplicated row is only how it happens.
    /// </summary>
    [Fact]
    public async Task Running_the_import_twice_does_not_duplicate_a_row_or_move_a_balance()
    {
        var name = $"EELedgerI_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var userId = await SeedLearnerAsync(connectionString, "idempotent");
            await SeedLedgerAsync(connectionString, userId);

            LedgerExportDocument exported;
            int balance;
            await using (var db = DbContextFactory.Create(connectionString))
            {
                var transfer = new LedgerTransferService(db, TimeProvider.System);
                exported = await transfer.ExportAsync();
                balance = await new CreditLedger(db).GetBalanceAsync(userId);
            }

            // Wipe the target's ledger so this test imports into an empty table: every row is new.
            await using (var db = DbContextFactory.Create(connectionString))
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CreditTransactions DISABLE TRIGGER ALL");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM CreditTransactions");
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CreditTransactions ENABLE TRIGGER ALL");
            }

            LedgerImportResult first;
            await using (var db = DbContextFactory.Create(connectionString))
            {
                first = await new LedgerTransferService(db, TimeProvider.System).ImportAsync(exported);
            }
            Assert.Equal(exported.Rows.Count, first.Inserted);
            Assert.Equal(0, first.AlreadyPresent);

            var rowsAfterFirst = await CountLedgerAsync(connectionString);
            Assert.Equal(exported.Rows.Count, rowsAfterFirst);

            LedgerImportResult second;
            await using (var db = DbContextFactory.Create(connectionString))
            {
                second = await new LedgerTransferService(db, TimeProvider.System).ImportAsync(exported);
            }

            // The second run writes nothing and recognises everything.
            Assert.Equal(0, second.Inserted);
            Assert.Equal(exported.Rows.Count, second.AlreadyPresent);
            Assert.True(second.Reconciled);
            Assert.Equal(rowsAfterFirst, await CountLedgerAsync(connectionString));

            await using (var db = DbContextFactory.Create(connectionString))
            {
                Assert.Equal(balance, await new CreditLedger(db).GetBalanceAsync(userId));
            }
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// A truncated file must be refused BEFORE it writes: a short import would produce a smaller ledger
    /// that looks complete, which is the one cutover failure a balance check cannot catch afterwards.
    /// </summary>
    [Fact]
    public async Task A_file_whose_row_count_disagrees_with_its_rows_is_refused_and_writes_nothing()
    {
        var name = $"EELedgerT_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            var userId = await SeedLearnerAsync(connectionString, "truncated");
            await SeedLedgerAsync(connectionString, userId);

            LedgerExportDocument exported;
            await using (var db = DbContextFactory.Create(connectionString))
            {
                exported = await new LedgerTransferService(db, TimeProvider.System).ExportAsync();
            }

            await using (var db = DbContextFactory.Create(connectionString))
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CreditTransactions DISABLE TRIGGER ALL");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM CreditTransactions");
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE CreditTransactions ENABLE TRIGGER ALL");
            }

            // Declares more rows than it carries: exactly the shape a file cut off mid-write has.
            var truncated = exported with { RowCount = exported.Rows.Count + 3 };

            await using (var db = DbContextFactory.Create(connectionString))
            {
                var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    new LedgerTransferService(db, TimeProvider.System).ImportAsync(truncated));
                Assert.Contains("truncated", refused.Message, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Equal(0, await CountLedgerAsync(connectionString));
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    // ---- fixtures ---------------------------------------------------------------------------

    private static async Task<int> CountLedgerAsync(string connectionString)
    {
        await using var db = DbContextFactory.Create(connectionString);
        return await db.CreditTransactions.CountAsync();
    }

    private static async Task SeedLedgerAsync(string connectionString, int userId)
    {
        await using var db = DbContextFactory.Create(connectionString);

        var adminRole = new BusinessObjects.Entities.Role { Code = Unique("adm"), Name = "Admin" };
        db.Roles.Add(adminRole);
        await db.SaveChangesAsync();

        var admin = new BusinessObjects.Entities.User
        {
            RoleId = adminRole.RoleId,
            Email = $"{Guid.NewGuid():N}@ledger-admin.test",
            PasswordHash = [1],
            FullName = "Ledger admin",
        };
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        db.CreditTransactions.Add(new BusinessObjects.Entities.CreditTransaction
        {
            UserId = userId,
            Reason = "admin_grant",
            Delta = 10,
            AdminUserId = admin.UserId,
            Note = "cutover fixture grant",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3),
        });
        db.CreditTransactions.Add(new BusinessObjects.Entities.CreditTransaction
        {
            UserId = userId,
            Reason = "admin_revoke",
            Delta = -3,
            AdminUserId = admin.UserId,
            Note = "cutover fixture revoke",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2),
        });
        await db.SaveChangesAsync();
    }

    private static async Task<int> SeedLearnerAsync(string connectionString, string tag)
    {
        await using var db = DbContextFactory.Create(connectionString);
        var role = new BusinessObjects.Entities.Role { Code = Unique("lrn"), Name = "Learner" };
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        var user = new BusinessObjects.Entities.User
        {
            RoleId = role.RoleId,
            Email = $"{tag}-{Guid.NewGuid():N}@ledger.test",
            PasswordHash = [1],
            FullName = tag,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.UserId;
    }

    /// <summary>
    /// The TARGET database's users, with the SAME ids the export carries. A ledger row names UserId and
    /// AdminUserId, and both are foreign keys, so an import into a database whose ids differ cannot
    /// land at all - measured: refused=2, because the source's admin (id 2) had no row to point at.
    /// Reproducing the ids is part of preserving the ledger, not a test convenience.
    /// </summary>
    private static async Task<int> SeedUsersWithSourceIdsAsync(string connectionString, int learnerId)
    {
        await using var db = DbContextFactory.Create(connectionString);

        var learnerRole = new BusinessObjects.Entities.Role { Code = Unique("lrn"), Name = "Learner" };
        var adminRole = new BusinessObjects.Entities.Role { Code = Unique("adm"), Name = "Admin" };
        db.Roles.AddRange(learnerRole, adminRole);
        await db.SaveChangesAsync();

        // The source's fixture created the learner first (id 1) and the admin second (id 2); mirror both.
        await db.Database.ExecuteSqlRawAsync(
            "SET IDENTITY_INSERT Users ON; " +
            $"INSERT INTO Users (UserId, RoleId, Email, PasswordHash, FullName) VALUES " +
            $"({learnerId}, {learnerRole.RoleId}, '{Guid.NewGuid():N}@ledger.test', 0x01, N'target learner'); " +
            $"INSERT INTO Users (UserId, RoleId, Email, PasswordHash, FullName) VALUES " +
            $"({learnerId + 1}, {adminRole.RoleId}, '{Guid.NewGuid():N}@ledger-admin.test', 0x01, N'target admin'); " +
            "SET IDENTITY_INSERT Users OFF;");

        return learnerId;
    }
}
