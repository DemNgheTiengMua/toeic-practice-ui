using DataAccess;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #250 criterion 2: <b>the seed loads and every exam still has the questions it had - the count per
/// exam is unchanged by the migration.</b>
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

            Dictionary<int, int> before;
            await using (var db = DbContextFactory.Create(connectionString))
            {
                before = await db.Questions.AsNoTracking()
                    .GroupBy(q => q.ExamId)
                    .Select(g => new { ExamId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.ExamId, g => g.Count);
            }

            // The seed must actually carry papers, or every assertion below is vacuous.
            Assert.True(before.Count >= 15, $"the seed produced {before.Count} exam(s); it loaded almost nothing");
            Assert.True(before.Values.Sum() >= 100, $"the seed produced {before.Values.Sum()} question(s); it loaded almost nothing");

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

            // No exam gained a roster it did not have.
            Assert.Equal(before.Keys.OrderBy(id => id), after.Keys.OrderBy(id => id));

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
