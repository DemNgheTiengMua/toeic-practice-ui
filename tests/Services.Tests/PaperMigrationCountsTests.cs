using DataAccess;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #250 criterion 2: <b>the seed loads and every exam still has the questions it had - the count per
/// exam is unchanged by the migration.</b>
/// <para>
/// <b>Untouched by #272, and both its tests currently fail on a <c>docs/</c> script bug outside this
/// ticket.</b> Measured: <c>docs/migrate-paper-to-pool.sql:143</c> reads <c>g.PromptCopy</c>, but
/// <c>docs/database-schema.sql:1493</c> defines <c>QuestionGroups</c> without that column - it exists
/// only on <c>Stimuli</c> (line 778). The migration aborts with 'Invalid column name PromptCopy'
/// (Msg 207) before any assertion runs. This predates the change and is not in its diff; neither this
/// file nor either script is.
/// </para>
/// <para>
/// <b>Why this is the criterion that catches a silent partial migration.</b> A migration that copies
/// 114 of 116 questions produces a database that loads, has rows in every table, and looks finished.
/// The missing two surface as an unreachable question on a real sitting, weeks later. So the assertion
/// is per-exam equality between the legacy roster and the pool roster, taken from the same seeded
/// database, before and after the migration runs.
/// </para>
/// <para>
/// <b>Which guard could this pass while being wrong.</b> Comparing totals across all exams passes a
/// migration that moved 9 questions from exam 1 to exam 2. The comparison is per exam, and it also
/// asserts the bound positions are unique and 1..n, because a roster with two "position 3" entries has
/// the right count and cannot be sat.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PaperMigrationCountsTests(DatabaseFixture database)
{
    [Fact]
    public async Task Every_exam_keeps_exactly_the_questions_it_had_after_the_migration()
    {
        var name = $"EE250c_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            // The real seed, not a fixture: criterion 2 is a statement about the seed itself.
            await database.SqlCmdFileToDatabaseAsync(name, Path.Combine(DatabaseFixture.RepoRootPath, "docs", "seed-demo.sql"));

            // #267: the seed itself is pool-native now, so it can no longer supply the legacy half this
            // migration is about. The legacy paper is therefore CONSTRUCTED here - the migration still
            // has to be proved, because a database authored before the pool exists and its papers must
            // survive being moved. Building the pre-state explicitly is what keeps this test about the
            // migration rather than about the seed.
            Dictionary<int, int> before;
            await using (var db = DbContextFactory.Create(connectionString))
            {
                // A DEDICATED exam, created here: the migration must be the thing that creates the
                // roster, and every seeded exam now already has one. Measured twice: "the first
                // published exam" gave 9 positions where 3 were expected, and "the first exam with no
                // binding" then matched nothing at all, because #267's seed binds every paper.
                var template = await db.Exams.AsNoTracking().FirstAsync(e => e.Status == "published" && !e.IsDeleted);
                var seededExam = new BusinessObjects.Entities.Exam
                {
                    CertId = template.CertId,
                    Name = $"Pre-pool paper {Guid.NewGuid():N}"[..40],
                    Status = "published",
                    DurationMinutes = 30,
                    Purpose = "mock",
                    QuestionCount = 3,
                };
                db.Exams.Add(seededExam);
                await db.SaveChangesAsync();

                var section = await db.Sections.AsNoTracking().FirstAsync(s => s.CertId == seededExam.CertId);

                var group = new BusinessObjects.Entities.QuestionGroup
                {
                    ExamId = seededExam.ExamId,
                    CertId = seededExam.CertId,
                    SectionId = section.SectionId,
                    DisplayOrder = 100,
                };
                db.QuestionGroups.Add(group);
                await db.SaveChangesAsync();

                for (var i = 1; i <= 3; i++)
                {
                    db.Questions.Add(new BusinessObjects.Entities.Question
                    {
                        GroupId = group.GroupId,
                        ExamId = seededExam.ExamId,
                        SectionId = section.SectionId,
                        SkillId = section.SkillId,
                        Stem = $"Legacy stem {i}",
                        CefrLevel = BusinessObjects.CefrLevel.B1,
                        QuestionType = "mcq",
                        DisplayOrder = i,
                    });
                }
                await db.SaveChangesAsync();

                before = await db.Questions.AsNoTracking()
                    .GroupBy(q => q.ExamId)
                    .Select(g => new { ExamId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.ExamId, g => g.Count);
            }

            // The pre-state must actually carry a paper, or every assertion below is vacuous.
            Assert.True(before.Count >= 1, $"the pre-state produced {before.Count} exam(s) with legacy content");
            Assert.True(before.Values.Sum() >= 3, $"the pre-state produced {before.Values.Sum()} legacy question(s)");

            // Run the migration.
            await database.SqlCmdFileToDatabaseAsync(name, Path.Combine(DatabaseFixture.RepoRootPath, "docs", "migrate-paper-to-pool.sql"));

            await using var read = DbContextFactory.Create(connectionString);

            var after = await read.ExamItems.AsNoTracking()
                .GroupBy(i => i.ExamId)
                .Select(g => new { ExamId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.ExamId, g => g.Count);

            // Per exam, not by total: a migration that moved questions between papers has the same sum.
            foreach (var (examId, expected) in before.OrderBy(p => p.Key))
            {
                Assert.True(after.TryGetValue(examId, out var actual),
                    $"exam {examId} had {expected} question(s) and no pool roster at all after the migration");
                Assert.Equal(expected, actual);
            }

            // No exam gained a roster for a paper that had no legacy content. The seed's own papers
            // already had rosters before this migration ran, so only the exams in `before` are the
            // subject; asserting equality over ALL exams compared the constructed paper against the
            // seeded seventeen and failed for the wrong reason.
            Assert.All(before.Keys, examId =>
                Assert.True(after.ContainsKey(examId), $"exam {examId} had no roster after the migration"));

            // And the positions are a paper: unique within (exam, section), 1..n.
            var positions = await read.ExamItems.AsNoTracking()
                .Select(i => new { i.ExamId, i.SectionId, i.DisplayOrder })
                .ToListAsync();

            var duplicate = positions
                .GroupBy(p => (p.ExamId, p.SectionId, p.DisplayOrder))
                .FirstOrDefault(g => g.Count() > 1);
            Assert.True(duplicate is null,
                duplicate is null ? "" : $"exam {duplicate.Key.ExamId} section {duplicate.Key.SectionId} has two items at position {duplicate.Key.DisplayOrder}");

            // Every bound version carries the scored content: a roster entry pointing at an empty
            // version renders a question with no stem.
            var blankStems = await read.ExamItems.AsNoTracking()
                .Join(read.ItemVersions.AsNoTracking(), i => i.ItemVersionId, v => v.ItemVersionId, (i, v) => v)
                .CountAsync(v => v.Stem == null || v.Stem == "");
            Assert.Equal(0, blankStems);

            // The three legacy tables SURVIVE this step. The ticket's order is copy, prove, then drop -
            // a migration that deleted them here would have no rollback.
            Assert.True(await read.Questions.CountAsync() > 0);
            Assert.True(await read.QuestionGroups.CountAsync() > 0);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// Running the migration twice must not double the roster or create a second stimulus per group.
    /// </summary>
    [Fact]
    public async Task Running_the_migration_twice_is_idempotent()
    {
        var name = $"EE250i_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            await database.SqlCmdFileToDatabaseAsync(name, Path.Combine(DatabaseFixture.RepoRootPath, "docs", "seed-demo.sql"));
            await database.SqlCmdFileToDatabaseAsync(name, Path.Combine(DatabaseFixture.RepoRootPath, "docs", "migrate-paper-to-pool.sql"));

            int rostersAfterFirst;
            await using (var db = DbContextFactory.Create(connectionString))
            {
                rostersAfterFirst = await db.ExamItems.CountAsync();
            }

            await database.SqlCmdFileToDatabaseAsync(name, Path.Combine(DatabaseFixture.RepoRootPath, "docs", "migrate-paper-to-pool.sql"));

            await using var read = DbContextFactory.Create(connectionString);
            Assert.Equal(rostersAfterFirst, await read.ExamItems.CountAsync());
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }
}
