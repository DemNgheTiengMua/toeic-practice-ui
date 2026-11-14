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

            async Task<int> CountAsync(string query)
            {
                await using var command = conn.CreateCommand();
                command.CommandText = query;
                return Convert.ToInt32(await command.ExecuteScalarAsync());
            }

            // #309: Strictly 2 active certificates (TOEIC 2-skill L&R, IELTS 4-skill L/R/S/W); VSTEP/TOEIC_CAT/TOEIC_SW purged.
            Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM Certificates WHERE IsActive = 1"));
            Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM Certificates WHERE Code IN ('VSTEP','TOEIC_CAT','TOEIC_SW')"));
            Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM Skills s JOIN Certificates c ON c.CertId=s.CertId WHERE c.Code='TOEIC'"));
            Assert.Equal(4, await CountAsync("SELECT COUNT(*) FROM Skills s JOIN Certificates c ON c.CertId=s.CertId WHERE c.Code='IELTS'"));

            // #309: 1 published Universal Placement Exam covering all 4 skills, and 1 published Mock Exam per certificate.
            Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM Exams WHERE Purpose='placement' AND Status='published'"));
            Assert.Equal(4, await CountAsync("""
                SELECT COUNT(DISTINCT ei.Modality) FROM ExamItems ei
                JOIN Exams e ON e.ExamId=ei.ExamId
                WHERE e.Purpose='placement' AND e.Status='published'
                """));
            Assert.Equal(2, await CountAsync("SELECT COUNT(*) FROM Exams WHERE Purpose='mock' AND Status='published'"));

            // #309: Flat GradingCriteria on IELTS Writing & Speaking.
            Assert.True(await CountAsync("""
                SELECT COUNT(*) FROM GradingCriteria gc
                JOIN Certificates c ON c.CertId=gc.CertId
                WHERE c.Code='IELTS'
                """) >= 8);

            // #309: Multi-format Items and Lessons tagged with 'ALL' and band ranges.
            Assert.Equal(7, await CountAsync("SELECT COUNT(DISTINCT RenderFormat) FROM Items"));
            Assert.True(await CountAsync("SELECT COUNT(*) FROM Items WHERE TargetBands = 'ALL'") > 0);
            Assert.True(await CountAsync("SELECT COUNT(*) FROM Items WHERE TargetBands <> 'ALL'") > 0);
            Assert.True(await CountAsync("SELECT COUNT(*) FROM Lessons WHERE TargetBands = 'ALL'") > 0);
            Assert.True(await CountAsync("SELECT COUNT(*) FROM Lessons WHERE TargetBands <> 'ALL'") > 0);

            Assert.Equal(4, await CountAsync("""
                SELECT COUNT(*) FROM Lessons
                WHERE Title IN (
                  'IELTS Listening: Follow Signposts and Corrections',
                  'IELTS Reading: Paraphrase Claims and Match Evidence',
                  'IELTS Task 1: Select and Describe Trends',
                  'IELTS Writing: Coherence, Cohesion, and Position')
                """));
            Assert.Equal(16, await CountAsync("""
                SELECT COUNT(*) FROM LessonCards c
                JOIN Lessons l ON l.LessonId=c.LessonId
                WHERE l.Title IN (
                  'IELTS Listening: Follow Signposts and Corrections',
                  'IELTS Reading: Paraphrase Claims and Match Evidence',
                  'IELTS Task 1: Select and Describe Trends',
                  'IELTS Writing: Coherence, Cohesion, and Position')
                """));
            // #232: PathModules and PathModuleItems are dropped. The demo path is no longer seeded:
            // #267 generates the seed from a migrated database, and a learning path is DERIVED from a
            // placement measurement the seed does not recreate - shipping the path without it would
            // assert a plan nobody made. The path machinery keeps its own tests
            // (LearningPathServiceTests), so the guarantee lives there rather than in a seed count.
            Assert.True(
                await CountAsync("""
                    SELECT COUNT(*) FROM LearnerPaths p
                    JOIN Users u ON u.UserId=p.UserId
                    WHERE u.Email='learner@demo.local'
                    """) == 0,
                "the seed must not ship a learning path it cannot justify with a measurement");

            // #240: a lesson is addressed by (skill, topic, level). A seeded lesson that left any of the
            // three unwritten would be unaddressable by the reader, and the NOT NULL columns would have
            // to be relaxed to make the seed load - so the seed is asserted to name a real cell.
            Assert.Equal(0, await CountAsync("""
                SELECT COUNT(*) FROM Lessons l
                WHERE l.TopicId IS NULL
                   OR l.CefrLevel IS NULL
                   OR l.Modality IS NULL
                   OR l.CefrLevel NOT IN ('A1','A2','B1','B2','C1','C2')
                """));
            Assert.True(await CountAsync("""
                SELECT COUNT(*) FROM Lessons l
                JOIN Topics t ON t.TopicId = l.TopicId AND t.Modality = l.Modality
                WHERE l.Title = N'Present Continuous Tense'
                """) > 0, "the demo lesson names a real (topic, level) cell");

            // 1. Verify demo accounts
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM Users WHERE Email IN ('admin@demo.local', 'learner@demo.local', 'learner.fresh@demo.local', 'mentor.khoa@demo.local')";
                var userCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.Equal(4, userCount);
            }

            // 2. Verify Credit Wallet & Ledger Invariant: balance is SUM(Delta) == 13 for learner@demo.local
            // #246 removed the seeded premium_path charge (-5) along with the reason code and the
            // LearnerPathId column, so the demo wallet is now the purchases (10 + 5) minus the one mock
            // exam start (-1) minus two mentor bookings and plus the admin grant: 13.
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT SUM(Delta) FROM CreditTransactions 
                                    WHERE UserId = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local')";
                var balance = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                Assert.Equal(13, balance);
            }

            // 3. #267: the demo path is generated rather than seeded. A path is derived from a placement
            //    measurement, and the generated seed does not ship a measurement nobody took, so the
            //    path is not seeded either - the learner generates one by sitting a placement. The path
            //    state machine keeps its own tests (LearningPathServiceTests), which is where a rule
            //    about done/in_progress/available/locked belongs.
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT COUNT(*) FROM LearnerPathSteps
                                    WHERE LearnerPathId IN (SELECT LearnerPathId FROM LearnerPaths WHERE Status = 'active')";
                Assert.Equal(0, Convert.ToInt32(await cmd.ExecuteScalarAsync()));
            }

            // 4. The accuracy views must be queryable on a freshly seeded database. What they RETURN is
            //    the learner's history, and the generated seed ships no answers (a measurement nobody
            //    took), so the assertion is that the views resolve and answer - not that rows exist.
            //    PoolAndLegacyAccuracyAgreeTests proves the arithmetic on answers it creates itself.
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT COUNT(*) FROM vw_UserSectionAccuracyAll
                                    WHERE UserId = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local')";
                Assert.True(Convert.ToInt32(await cmd.ExecuteScalarAsync()) >= 0);
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT COUNT(*) FROM vw_UserTopicAccuracy
                                    WHERE UserId = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local')";
                Assert.True(Convert.ToInt32(await cmd.ExecuteScalarAsync()) >= 0);
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
                                    WHERE MentorId = (SELECT MentorId FROM MentorProfiles WHERE DisplayName = N'Dr. Trần Minh Khoa')";
                var rating = Convert.ToDecimal(await cmd.ExecuteScalarAsync());
                Assert.Equal(5.00m, rating);
            }

            // 7. #224: the demo learner's bank is two POOL links, each pinned to an item version. The
            //    count is the one docs/MANUAL-TEST-CREDENTIALS.md advertises, and the pin is checked on
            //    the row so a link that stored a version it does not read would still be caught here.
            Assert.Equal(2, await CountAsync("""
                SELECT COUNT(*) FROM SavedQuestions sq
                JOIN ItemVersions v ON v.ItemVersionId = sq.ItemVersionId AND v.ItemId = sq.ItemId
                WHERE sq.UserId = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local')
                  AND sq.CertId = (SELECT ActiveCertId FROM Users WHERE Email = 'learner@demo.local')
                """));

            // 8. #267 / D23: a legacy saved link is DROPPED, not re-pointed. #269 removed the
            //    QuestionId column that rule was about, so the seed has no legacy shape left to delete
            //    and the claim is now a schema fact rather than a row. It is asserted HERE rather than by
            //    planting a row: the column is gone, so a planted legacy link cannot be written at all.
            //    The second query is the control - a catalogue lookup that is answering nothing would
            //    pass the absence assertion vacuously, and this is the shape the bank does have.
            Assert.Equal(0, await CountAsync("""
                SELECT COUNT(*) FROM sys.columns
                WHERE object_id = OBJECT_ID('dbo.SavedQuestions') AND name = 'QuestionId'
                """));
            Assert.Equal(1, await CountAsync("""
                SELECT COUNT(*) FROM sys.columns
                WHERE object_id = OBJECT_ID('dbo.SavedQuestions') AND name = 'ItemVersionId'
                """));
            Assert.Equal(2, await CountAsync("""
                SELECT COUNT(*) FROM SavedQuestions
                WHERE UserId = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local')
                """));
        }
        finally
        {
            await fixture.DropDatabaseAsync(dbName);
        }
    }
}
