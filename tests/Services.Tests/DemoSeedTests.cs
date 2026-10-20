using Microsoft.Data.SqlClient;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class DemoSeedTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task SeedDemoSql_ExecutesIdempotently_AndPreservesInvariants()
    {
        var dbName = $"DemoSeedTest_{Guid.NewGuid():N}";
        try
        {
            await fixture.CreateFreshDatabaseAsync(dbName);
            var seedPath = Path.Combine(DatabaseFixture.RepoRootPath, "docs", "seed-demo.sql");

            // First run: seeds all demo catalog, accounts, and rich history
            await fixture.SqlCmdFileToDatabaseAsync(dbName, seedPath);

            // Second run: tests idempotency (no PK or unique constraint violations)
            await fixture.SqlCmdFileToDatabaseAsync(dbName, seedPath);

            // Connect and verify invariants
            // Point at the fresh database by name, whatever the fixture's own (TEST_DB_NAME) is.
            var connStr = new SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = dbName }.ConnectionString;
            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // 1. Verify demo accounts
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM Users WHERE Email IN ('admin@demo.local', 'learner@demo.local', 'learner.fresh@demo.local', 'mentor.khoa@demo.local')";
                var userCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.Equal(4, userCount);
            }

            // 2. Verify Credit Wallet & Ledger Invariant: balance is SUM(Delta) == 8 for learner@demo.local
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT SUM(Delta) FROM CreditTransactions 
                                    WHERE UserId = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local')";
                var balance = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.Equal(8, balance);
            }

            // 3. Verify Active Learning Path has all 4 statuses represented across steps
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT Status, COUNT(*) FROM LearnerPathSteps 
                                    WHERE LearnerPathId = (SELECT TOP 1 LearnerPathId FROM LearnerPaths WHERE Status = 'active')
                                    GROUP BY Status";
                var statuses = new Dictionary<string, int>();
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    statuses[reader.GetString(0)] = reader.GetInt32(1);
                }

                Assert.True(statuses.ContainsKey("done") && statuses["done"] >= 1, "Should have completed steps");
                Assert.True(statuses.ContainsKey("in_progress") && statuses["in_progress"] >= 1, "Should have in_progress steps");
                Assert.True(statuses.ContainsKey("available") && statuses["available"] >= 1, "Should have available steps");
                Assert.True(statuses.ContainsKey("locked") && statuses["locked"] >= 1, "Should have locked steps");
            }

            // 4. Verify Accuracy Views (vw_UserSectionAccuracyAll, vw_UserCategoryAccuracy) populate
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT COUNT(*) FROM vw_UserSectionAccuracyAll 
                                    WHERE UserId = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local')";
                var sectionAccCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.True(sectionAccCount >= 3, "Should have section accuracy rows for P1, P2, P5");
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT COUNT(*) FROM vw_UserCategoryAccuracy 
                                    WHERE UserId = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local')";
                var catAccCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.True(catAccCount >= 2, "Should have category accuracy rows");
            }

            // 5. Verify Complaints resolved with make-good grant
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT COUNT(*) FROM Complaints 
                                    WHERE Status = 'resolved' AND ResolvedByGrantTxnId IS NOT NULL";
                var complaintCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.True(complaintCount >= 1, "Should have a resolved complaint referencing admin grant");
            }

            // 6. Verify Mentors and average rating view
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT AvgRating FROM vw_MentorAvgRating 
                                    WHERE MentorId = (SELECT MentorId FROM MentorProfiles WHERE UserId = (SELECT UserId FROM Users WHERE Email = 'mentor.khoa@demo.local'))";
                var rating = Convert.ToDecimal(await cmd.ExecuteScalarAsync());
                Assert.Equal(5.00m, rating);
            }
        }
        finally
        {
            await fixture.DropDatabaseAsync(dbName);
        }
    }
}
