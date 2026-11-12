using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #276 criterion 4, measured on the SHIPPED SEED: <b>is the static placement paper still what a learner
/// sits?</b>
/// <para>
/// <b>Why this is a test and not a sentence in the pull request.</b> The wiring is proved on a fixture
/// whose placement paper binds the stage-one stock, which is the arrangement the draw needs. The seed is a
/// different database, and a fixture cannot say what it holds. Measured on the seed: the draw's 24 eligible
/// items are bound to <b>no exam at all</b>, so every candidate is unservable and the sitting falls back to
/// the static paper. Criterion 4 is therefore FALSE on the shipped product while every fixture test is
/// green - which is exactly the kind of claim this file exists to keep honest.
/// </para>
/// <para>
/// <b>Why it is skipped rather than failing.</b> The cause is content, not code, on the same reasoning
/// #275 was closed under: no roster binding exists, and stock and assembly are supplied by another party.
/// A permanently red test would be a test the team learns to ignore, so this carries a Skip that names the
/// measurement and the fix. Removing one attribute is the acceptance step - the assertions need no change.
/// </para>
/// <para>
/// <b>What it asserts, and what a wrong wiring could still pass.</b> "The attempt was created" passes on
/// the static paper. The assertion is which positions the sitting was served: if the draw landed, the paper
/// holds six bands and its positions are NOT the roster positions the paper binds. If it fell back, the
/// served set is empty - and the message says so in those words.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PlacementDrawOnTheShippedSeedTests(DatabaseFixture database)
{
    /// <summary>
    /// The measurement, stated as a passing test so it cannot go stale: how much of the draw's stock this
    /// paper can actually serve. Today it is zero, and that is the whole reason the test below is skipped.
    /// </summary>
    [Fact]
    public async Task The_seed_binds_none_of_the_stage_one_stock_to_a_placement_paper()
    {
        var name = $"EE276SeedB_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            await database.SqlCmdFileToDatabaseAsync(
                name, Path.Combine(DatabaseFixture.RepoRootPath, "docs", "seed-demo.sql"));

            await using var db = DbContextFactory.Create(connectionString);

            var eligible = await db.Items.AsNoTracking()
                .Where(i => i.PlacementEligible && i.LifecycleStatus == "active")
                .Select(i => i.ItemId)
                .ToListAsync();

            // The control: the stage-one draw is feedable. Without this, "zero bound" would also hold on a
            // seed that had no stock at all, and would prove nothing about assembly.
            Assert.NotEmpty(eligible);

            var bound = await db.ExamItems.AsNoTracking()
                .Where(i => eligible.Contains(i.ItemId))
                .Select(i => i.ItemId)
                .Distinct()
                .ToListAsync();

            Assert.Empty(bound);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    [Fact(Skip =
        "#276 criterion 4 is content-blocked, not code-blocked: the seed binds 0 of its 24 placement-eligible "
        + "items to any exam, so AttemptItemResponses' foreign key into ExamItems(ExamItemId, ExamId) refuses "
        + "every drawn position and the sitting falls back to the static paper. The sibling test above is the "
        + "measurement. Unskip when the placement paper's roster binds the stage-one stock - the assertion "
        + "needs no change.")]
    public async Task A_learner_sitting_the_TOEIC_placement_is_served_the_draw_not_the_static_paper()
    {
        var name = $"EE276Seed_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            await database.SqlCmdFileToDatabaseAsync(
                name, Path.Combine(DatabaseFixture.RepoRootPath, "docs", "seed-demo.sql"));

            int examId, userId, rosterCount;
            await using (var setup = DbContextFactory.Create(connectionString))
            {
                var toeicCertId = await setup.Certificates
                    .Where(c => c.Code == "TOEIC").Select(c => c.CertId).SingleAsync();

                examId = await setup.Exams.AsNoTracking()
                    .Where(e => e.Purpose == "placement" && e.Status == "published" && !e.IsDeleted
                        && e.CertId == toeicCertId)
                    .OrderBy(e => e.ExamId)
                    .Select(e => e.ExamId)
                    .FirstAsync();

                rosterCount = await setup.ExamItems.CountAsync(i => i.ExamId == examId);
                userId = await setup.Users.AsNoTracking()
                    .Where(u => u.Email == "learner.fresh@demo.local")
                    .Select(u => u.UserId)
                    .SingleAsync();
            }

            int attemptId;
            await using (var db = DbContextFactory.Create(connectionString))
            {
                var service = new PlacementService(db, new LearnerService(db));
                attemptId = (await service.StartPlacementTestAsync(userId, examId)).AttemptId;
            }

            await using var read = DbContextFactory.Create(connectionString);

            // Empty means "the exam's own roster", i.e. the static paper - the state criterion 4 forbids.
            var served = await read.AttemptItemResponses.AsNoTracking()
                .Where(r => r.AttemptId == attemptId)
                .OrderBy(r => r.PositionNumber)
                .Select(r => r.ExamItemId)
                .ToListAsync();

            Assert.True(
                served.Count > 0,
                $"the placement sitting on the shipped seed was served NO draw: it fell back to the exam's "
                + $"own {rosterCount} roster position(s), so the static paper is still what a learner sits. "
                + "A draw is servable only when this exam's roster binds the stock it selects.");

            // Every objective skill's full stage-one share.
            Assert.Equal(2 * PlacementStageOneBlueprint.ItemsPerObjectiveSkill, served.Count);

            // Six bands, which the static paper cannot produce.
            var bands = await read.ExamItems.AsNoTracking()
                .Where(i => served.Contains(i.ExamItemId))
                .Join(read.Items.AsNoTracking(), i => i.ItemId, item => item.ItemId, (i, item) => item.CefrLevel)
                .Distinct()
                .ToListAsync();
            Assert.Equal(CefrLevel.AuthoredLevels.Count, bands.Count);
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }
}
