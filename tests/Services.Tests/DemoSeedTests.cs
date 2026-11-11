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

            // #156: the two toy TOEIC placement exams (4 and 2 questions) are gone, and the one that
            // replaced them is long enough for a band to mean something: at least 20 real items,
            // across every Listening and Reading part, at more than one difficulty.
            Assert.Equal(1, await CountAsync("""
                SELECT COUNT(*) FROM Exams
                WHERE Name = 'TOEIC Placement Test — Diagnostic'
                  AND Purpose='placement' AND Status='published' AND QuestionCount >= 20
                """));
            // #267: the seed's paper content is the pool now. Same assertion, pool source.
            Assert.Equal(24, await CountAsync("""
                SELECT COUNT(*) FROM ExamItems ei
                JOIN Exams e ON e.ExamId=ei.ExamId
                WHERE e.Name='TOEIC Placement Test — Diagnostic'
                """));
            Assert.Equal(7, await CountAsync("""
                SELECT COUNT(DISTINCT ei.SectionId) FROM ExamItems ei
                JOIN Exams e ON e.ExamId=ei.ExamId
                WHERE e.Name='TOEIC Placement Test — Diagnostic'
                """));
            // #230: the diagnostic must span more than one level, for the same reason it had to span
            // more than one difficulty: a paper stratified on one band cannot separate the bands above
            // it. The count reads the level, and the threshold is unchanged.
            Assert.True(await CountAsync("""
                SELECT COUNT(DISTINCT i.CefrLevel) FROM ExamItems ei
                JOIN Exams e ON e.ExamId=ei.ExamId
                JOIN Items i ON i.ItemId=ei.ItemId
                WHERE e.Name='TOEIC Placement Test — Diagnostic'
                """) >= 3, "the diagnostic must carry more than one level");
            Assert.Equal(0, await CountAsync("""
                SELECT COUNT(*) FROM Exams
                WHERE Purpose='placement' AND CertId=(SELECT CertId FROM Certificates WHERE Code='TOEIC')
                  AND QuestionCount < 20
                """));
            // The card copy must not claim the exam is adaptive: it is not. (TOEIC_CAT genuinely is
            // a different, adaptive certificate, so the assertion is scoped to placement exams.)
            Assert.Equal(0, await CountAsync("""
                SELECT COUNT(*) FROM Exams WHERE Purpose='placement' AND Description LIKE '%daptive%'
                """));
            Assert.Equal(20, await CountAsync("""
                SELECT COUNT(*) FROM ExamItems ei
                JOIN Exams e ON e.ExamId=ei.ExamId
                JOIN Items i ON i.ItemId=ei.ItemId
                WHERE e.Name IN ('IELTS-DIAG-01','VSTEP-DIAG-01') AND e.Purpose='placement' AND i.RenderFormat='mcq'
                """));
            // A pool version carries one rationale per option column, so "eighty option rows with a
            // rationale" becomes "twenty versions whose four rationales are all present".
            Assert.Equal(20, await CountAsync("""
                SELECT COUNT(*) FROM ExamItems ei
                JOIN Exams e ON e.ExamId=ei.ExamId
                JOIN ItemVersions v ON v.ItemVersionId=ei.ItemVersionId
                WHERE e.Name IN ('IELTS-DIAG-01','VSTEP-DIAG-01')
                  AND v.RationaleA IS NOT NULL AND v.RationaleB IS NOT NULL
                  AND v.RationaleC IS NOT NULL AND v.RationaleD IS NOT NULL
                """));
            Assert.Equal(2, await CountAsync("""
                SELECT COUNT(*) FROM ExamSectionAudio a
                JOIN Exams e ON e.ExamId=a.ExamId
                WHERE e.Name IN ('IELTS-DIAG-01','VSTEP-DIAG-01')
                """));
            // #267: a pool position's audio slice and transcript live on its stimulus, not a question
            // group. Same assertion: eight positions in the two diagnostics carry all three.
            Assert.Equal(8, await CountAsync("""
                SELECT COUNT(*) FROM ExamItems ei
                JOIN Exams e ON e.ExamId=ei.ExamId
                JOIN Items i ON i.ItemId=ei.ItemId
                JOIN Stimuli st ON st.StimulusId=i.StimulusId
                WHERE e.Name IN ('IELTS-DIAG-01','VSTEP-DIAG-01')
                  AND st.AudioStartMs IS NOT NULL AND st.AudioEndMs IS NOT NULL AND st.Transcript IS NOT NULL
                """));
            Assert.Equal(2, await CountAsync("""
                SELECT COUNT(*) FROM Exams
                WHERE Name IN ('IELTS-DIAG-01 Writing Extension','VSTEP-DIAG-01 Writing Extension')
                  AND Purpose='writing_practice' AND SourceName='Free diagnostic benchmark'
                """));
            Assert.Equal(4, await CountAsync("""
                SELECT COUNT(*) FROM ExamItems ei
                JOIN Exams e ON e.ExamId=ei.ExamId
                JOIN Items i ON i.ItemId=ei.ItemId
                JOIN ItemVersions v ON v.ItemVersionId=ei.ItemVersionId
                WHERE e.Name IN ('IELTS-DIAG-01 Writing Extension','VSTEP-DIAG-01 Writing Extension')
                  AND i.RenderFormat='free_response' AND v.MinWords IS NOT NULL
                """));
            Assert.Equal(8, await CountAsync("""
                SELECT COUNT(*) FROM GradingCriteria c
                JOIN Exams e ON e.CertId=c.CertId
                JOIN Skills s ON s.SkillId=c.SkillId AND s.CertId=e.CertId AND s.Code='WRIT'
                WHERE e.Name IN ('IELTS-DIAG-01 Writing Extension','VSTEP-DIAG-01 Writing Extension')
                """));
            // #232: the diagnostic sprint seeds its lessons and cards; the path steps that use them are
            // planned at run time, so there is no template row to count here any more.
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
                                    WHERE MentorId = (SELECT MentorId FROM MentorProfiles WHERE UserId = (SELECT UserId FROM Users WHERE Email = 'mentor.khoa@demo.local'))";
                var rating = Convert.ToDecimal(await cmd.ExecuteScalarAsync());
                Assert.Equal(5.00m, rating);
            }

            // 7. #224: the demo learner's bank is two POOL links, each pinned to an item version, and
            //    no legacy link survives (D23 drops them rather than re-pointing them). The count is
            //    the one docs/MANUAL-TEST-CREDENTIALS.md advertises, and the pin is checked on the row
            //    so a link that stored a version it does not read would still be caught here.
            Assert.Equal(2, await CountAsync("""
                SELECT COUNT(*) FROM SavedQuestions sq
                JOIN ItemVersions v ON v.ItemVersionId = sq.ItemVersionId AND v.ItemId = sq.ItemId
                WHERE sq.UserId = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local')
                  AND sq.QuestionId IS NULL
                  AND sq.CertId = (SELECT ActiveCertId FROM Users WHERE Email = 'learner@demo.local')
                """));
            Assert.Equal(0, await CountAsync("""
                SELECT COUNT(*) FROM SavedQuestions
                WHERE UserId = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local')
                  AND QuestionId IS NOT NULL
                """));

            // 8. #267 / D23: a legacy saved link is DROPPED, not re-pointed. The generated seed carries
            //    that rule as an explicit DELETE, because it is a statement about a database that
            //    predates the pool - not a row a dump could carry. It is asserted HERE rather than by
            //    planting a row: a planted link needs a real Questions row to satisfy its foreign key,
            //    and the seed no longer creates one, so a planted fixture would be testing a database
            //    shape the product no longer has.
            Assert.True(await CountAsync("""
                SELECT COUNT(*) FROM SavedQuestions
                WHERE UserId = (SELECT UserId FROM Users WHERE Email = 'learner@demo.local')
                  AND QuestionId IS NOT NULL
                """) == 0, "the seed must leave no legacy saved link behind");
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
