using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #250 / #268: <b>the seed can feed a placement stage-one draw.</b>
/// <para>
/// Stage one is a fixed blueprint - one item at A1, A2, C1, C2 and two at B1, B2, per objective skill.
/// Measured before this ticket, the seed held <c>A2 = 1</c> and <c>B1 = 31</c>, nothing at all at A1,
/// C1 or C2 for reading or listening, and <b>zero</b> placement-eligible items anywhere. The two-stage
/// design was implemented, tested, and could not be fed.
/// </para>
/// <para>
/// <b>Why this asserts on deficits rather than on a count.</b> A count says the seed holds items; the
/// blueprint's own deficit report says whether a <i>draw</i> can be satisfied. Asserting a count would
/// pass on a seed whose stock sits at the wrong bands.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SeedFeedsPlacementTests(DatabaseFixture database)
{
    [Fact]
    public async Task Every_objective_skill_can_draw_a_complete_stage_one_from_the_seed()
    {
        var name = $"EE268P_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            await database.SqlCmdFileToDatabaseAsync(
                name, Path.Combine(DatabaseFixture.RepoRootPath, "docs", "seed-demo.sql"));

            await using var db = DbContextFactory.Create(connectionString);

            // The precondition: eligible stock exists at all. Without it every draw below is empty and
            // "no deficits" would be vacuously true for a blueprint of zero items.
            var eligibleTotal = await db.Items.CountAsync(i => i.PlacementEligible);
            Assert.True(eligibleTotal > 0, "the seed holds no placement-eligible item at all");

            foreach (var modality in PlacementStageOneBlueprint.ObjectiveModalities)
            {
                var modalityCode = modality.ToCode();
                var candidates = await db.Items.AsNoTracking()
                    .Where(i => i.PlacementEligible
                        && i.LifecycleStatus == "active"
                        && i.Modality == modalityCode)
                    .Select(i => new { i.ItemId, i.CefrLevel })
                    .ToListAsync();

                Assert.True(candidates.Count >= PlacementStageOneBlueprint.ItemsPerObjectiveSkill,
                    $"{modalityCode} holds {candidates.Count} eligible item(s); stage one needs "
                    + $"{PlacementStageOneBlueprint.ItemsPerObjectiveSkill}");

                // Every level the seed claims must be authored; PreA1 would silently drop a band.
                var parsed = candidates
                    .Select(c => new PlacementStageOneCandidate(c.ItemId, c.CefrLevel))
                    .ToList();
                Assert.All(parsed, p => Assert.Contains(p.Level, CefrLevel.AuthoredLevels));

                var selection = PlacementStageOneBlueprint.Select(parsed);

                // The blueprint's own report: a band short of its count is a deficit to name.
                Assert.True(selection.IsComplete,
                    $"{modalityCode} stage one is short at: "
                    + string.Join(", ", selection.Deficits.Select(d => $"{d.Level.Code} {d.Drawn}/{d.Wanted}")));

                Assert.Equal(PlacementStageOneBlueprint.ItemsPerObjectiveSkill, selection.ItemCount);
            }
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// The stock is real content of the band it claims, not one stem copied with a changed level: a
    /// routing instrument whose "C2" item reads like its "A1" item routes nobody.
    /// </summary>
    [Fact]
    public async Task The_eligible_stock_reads_differently_at_each_band()
    {
        var name = $"EE268R_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            await database.SqlCmdFileToDatabaseAsync(
                name, Path.Combine(DatabaseFixture.RepoRootPath, "docs", "seed-demo.sql"));

            await using var db = DbContextFactory.Create(connectionString);

            var byBand = await db.Items.AsNoTracking()
                .Where(i => i.PlacementEligible && i.LifecycleStatus == "active")
                .Select(i => new
                {
                    i.CefrLevel,
                    i.Modality,
                    Stems = i.ItemVersions.Select(v => v.Stem).ToList(),
                })
                .ToListAsync();

            // Every eligible item carries a stem at all.
            Assert.All(byBand, i => Assert.NotEmpty(i.Stems));

            // No two eligible items share a stem, per skill: a duplicate would be the same item claiming
            // two bands, which is the one thing a routing instrument cannot do.
            foreach (var group in byBand.GroupBy(i => i.Modality))
            {
                var stems = group.SelectMany(i => i.Stems).ToList();
                Assert.Equal(stems.Count, stems.Distinct().Count());
            }
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }
}
