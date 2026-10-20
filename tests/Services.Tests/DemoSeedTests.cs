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

            Assert.Equal(2, await CountAsync("""
                SELECT COUNT(*) FROM Exams
                WHERE (Name='IELTS-DIAG-01' AND Purpose='placement' AND DurationMinutes=60 AND QuestionCount=10)
                   OR (Name='VSTEP-DIAG-01' AND Purpose='placement' AND DurationMinutes=45 AND QuestionCount=10)
                """));
            Assert.Equal(20, await CountAsync("""
                SELECT COUNT(*) FROM Questions q
                JOIN Exams e ON e.ExamId=q.ExamId
                WHERE e.Name IN ('IELTS-DIAG-01','VSTEP-DIAG-01') AND e.Purpose='placement' AND q.QuestionType='mcq'
                """));
            Assert.Equal(80, await CountAsync("""
                SELECT COUNT(*) FROM QuestionOptions o
                JOIN Questions q ON q.QuestionId=o.QuestionId
                JOIN Exams e ON e.ExamId=q.ExamId
                WHERE e.Name IN ('IELTS-DIAG-01','VSTEP-DIAG-01')
                  AND q.QuestionType='mcq' AND o.Rationale IS NOT NULL
                """));
            Assert.Equal(2, await CountAsync("""
                SELECT COUNT(*) FROM ExamSectionAudio a
                JOIN Exams e ON e.ExamId=a.ExamId
                WHERE e.Name IN ('IELTS-DIAG-01','VSTEP-DIAG-01')
                """));
            Assert.Equal(8, await CountAsync("""
                SELECT COUNT(*) FROM QuestionGroups g
                JOIN Exams e ON e.ExamId=g.ExamId
                WHERE e.Name IN ('IELTS-DIAG-01','VSTEP-DIAG-01')
                  AND g.AudioStartMs IS NOT NULL AND g.AudioEndMs IS NOT NULL AND g.Transcript IS NOT NULL
                """));
            Assert.Equal(2, await CountAsync("""
                SELECT COUNT(*) FROM Exams
                WHERE Name IN ('IELTS-DIAG-01 Writing Extension','VSTEP-DIAG-01 Writing Extension')
                  AND Purpose='writing_practice' AND SourceName='Free diagnostic benchmark'
                """));
            Assert.Equal(4, await CountAsync("""
                SELECT COUNT(*) FROM Questions q
                JOIN Exams e ON e.ExamId=q.ExamId
                WHERE e.Name IN ('IELTS-DIAG-01 Writing Extension','VSTEP-DIAG-01 Writing Extension')
                  AND q.QuestionType='free_response' AND q.MinWords IS NOT NULL
                """));
            Assert.Equal(8, await CountAsync("""
                SELECT COUNT(*) FROM GradingCriteria c
                JOIN Exams e ON e.CertId=c.CertId
                JOIN Skills s ON s.SkillId=c.SkillId AND s.CertId=e.CertId AND s.Code='WRIT'
                WHERE e.Name IN ('IELTS-DIAG-01 Writing Extension','VSTEP-DIAG-01 Writing Extension')
                """));
            Assert.Equal(2, await CountAsync("""
                SELECT COUNT(*) FROM PathTemplates
                WHERE Name IN ('IELTS Diagnostic Remedial Sprint','VSTEP Diagnostic Remedial Sprint')
                """));
            Assert.Equal(8, await CountAsync("""
                SELECT COUNT(*) FROM Lessons
                WHERE Title IN (
                  'IELTS Listening: Follow Signposts and Corrections',
                  'IELTS Reading: Paraphrase Claims and Match Evidence',
                  'IELTS Task 1: Select and Describe Trends',
                  'IELTS Writing: Coherence, Cohesion, and Position',
                  'VSTEP Listening: Catch Purpose, Time, and Changes',
                  'VSTEP Reading: Locate Paraphrases and References',
                  'VSTEP Writing: Organise a Formal Email',
                  'VSTEP Writing: Build and Support an Argument')
                """));
            Assert.Equal(32, await CountAsync("""
                SELECT COUNT(*) FROM LessonCards c
                JOIN Lessons l ON l.LessonId=c.LessonId
                WHERE l.Title IN (
                  'IELTS Listening: Follow Signposts and Corrections',
                  'IELTS Reading: Paraphrase Claims and Match Evidence',
                  'IELTS Task 1: Select and Describe Trends',
                  'IELTS Writing: Coherence, Cohesion, and Position',
                  'VSTEP Listening: Catch Purpose, Time, and Changes',
                  'VSTEP Reading: Locate Paraphrases and References',
                  'VSTEP Writing: Organise a Formal Email',
                  'VSTEP Writing: Build and Support an Argument')
                """));
            Assert.Equal(8, await CountAsync("""
                SELECT COUNT(*) FROM Lessons l
                WHERE l.Title IN (
                  'IELTS Listening: Follow Signposts and Corrections',
                  'IELTS Reading: Paraphrase Claims and Match Evidence',
                  'IELTS Task 1: Select and Describe Trends',
                  'IELTS Writing: Coherence, Cohesion, and Position',
                  'VSTEP Listening: Catch Purpose, Time, and Changes',
                  'VSTEP Reading: Locate Paraphrases and References',
                  'VSTEP Writing: Organise a Formal Email',
                  'VSTEP Writing: Build and Support an Argument')
                  AND (SELECT COUNT(*) FROM LessonCards c WHERE c.LessonId=l.LessonId)=4
                  AND (SELECT COUNT(DISTINCT CardType) FROM LessonCards c WHERE c.LessonId=l.LessonId)=4
                """));
            Assert.Equal(8, await CountAsync("""
                SELECT COUNT(*) FROM PathModules m
                JOIN PathTemplates t ON t.TemplateId=m.TemplateId AND t.CertId=m.CertId
                WHERE t.Name IN ('IELTS Diagnostic Remedial Sprint','VSTEP Diagnostic Remedial Sprint')
                """));
            Assert.Equal(4, await CountAsync("""
                SELECT COUNT(*) FROM PathModuleItems i
                JOIN PathModules m ON m.ModuleId=i.ModuleId
                JOIN PathTemplates t ON t.TemplateId=m.TemplateId AND t.CertId=m.CertId
                WHERE t.Name IN ('IELTS Diagnostic Remedial Sprint','VSTEP Diagnostic Remedial Sprint')
                  AND i.ItemType='ai_task' AND i.IsPremiumOnly=0
                """));

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
