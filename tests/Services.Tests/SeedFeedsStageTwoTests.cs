using System;
using System.Collections.Generic;
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
/// #275: <b>every route in <see cref="PlacementStageTwoBlueprint"/> can draw a complete stage two from
/// the seed.</b>
/// <para>
/// <b>Why this file exists.</b> <c>SeedFeedsPlacementTests</c> proves stage ONE and that is exactly why
/// #260's "stock is solved" reading was wrong: it asks the blueprints for a complete stage-one draw, and
/// stage two was never asked. Wiring the draw before stage two can draw would refuse 4 of 5 routes and
/// leave those learners with no sitting at all - strictly worse than the static paper, which at least
/// always produces one.
/// </para>
/// <para>
/// <b>What a wrong implementation could still pass.</b> Counting rows passes on stock that is the right
/// NUMBER and the wrong BAND: 7 C1 items do not let a C1 route draw if stage one already served them.
/// So the assertions are the BLUEPRINT's own report - <c>Select</c> over the real candidates, then
/// <c>IsComplete</c> and the named deficits - which is the same proof style the stage-one file uses and
/// the ticket asks for.
/// </para>
/// <para>
/// <b>It asks per ROUTE, not per (skill, band).</b> A route delivers 5 or 6 items across a routed PAIR;
/// what it needs is that pair's delivery plus stage one's burn, which is why
/// <c>MinimumStageTwoItemsAtABand</c> exists rather than a flat 7.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SeedFeedsStageTwoTests(DatabaseFixture database)
{
    [Fact]
    public async Task Every_route_can_draw_a_complete_stage_two_from_the_seed()
    {
        var name = $"EE275P_{Guid.NewGuid():N}"[..24];
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

            var shorts = new List<string>();

            foreach (var modality in PlacementStageOneBlueprint.ObjectiveModalities)
            {
                var modalityCode = modality.ToCode();

                var candidates = await db.Items.AsNoTracking()
                    .Where(i => i.PlacementEligible
                        && i.LifecycleStatus == "active"
                        && i.Modality == modalityCode)
                    .Select(i => new { i.ItemId, i.CefrLevel, i.TopicId })
                    .ToListAsync();

                var pool = candidates
                    .Select(c => new PlacementStageTwoStock(c.ItemId, c.CefrLevel, c.TopicId))
                    .ToList();

                foreach (var route in PlacementStageTwoBlueprint.Routes)
                {
                    // Stage one has already served one item per band of the route, so this asks the
                    // blueprint against the SAME pool with those served ids marked seen - which is the
                    // ticket's question ("can stage two draw AFTER stage one") rather than a stock count.
                    var seen = new HashSet<int>();
                    foreach (var (level, _) in route.Delivery)
                    {
                        var burned = pool.FirstOrDefault(p => p.Level == level);
                        if (burned is not null)
                        {
                            seen.Add(burned.ItemId);
                        }
                    }

                    var afterStageOne = pool
                        .Select(p => p with { TimesSeen = seen.Contains(p.ItemId) ? 1 : p.TimesSeen })
                        .ToList();

                    var selection = PlacementStageTwoBlueprint.Select(afterStageOne, route);

                    if (!selection.IsComplete)
                    {
                        shorts.Add($"{modalityCode} {route.Code}: "
                            + string.Join(", ", selection.Deficits.Select(d => $"{d.Level.Code} {d.Drawn}/{d.Wanted}")));
                    }
                }
            }

            Assert.True(shorts.Count == 0,
                "stage two cannot draw on " + shorts.Count + " route(s), so wiring it would leave those "
                + "learners with no sitting:" + Environment.NewLine + string.Join(Environment.NewLine, shorts));
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }

    /// <summary>
    /// The stock is real content of the band it claims, not one stem copied with a changed level. A
    /// routing instrument whose C2 item reads like its A1 item routes nobody - and stage two is the half
    /// that has to be precise, because it is what decides the final band.
    /// </summary>
    [Fact]
    public async Task No_two_eligible_items_share_a_stem_within_a_skill()
    {
        var name = $"EE275S_{Guid.NewGuid():N}"[..24];
        var connectionString = await database.CreateFreshDatabaseAsync(name);
        try
        {
            await database.SqlCmdFileToDatabaseAsync(
                name, Path.Combine(DatabaseFixture.RepoRootPath, "docs", "seed-demo.sql"));

            await using var db = DbContextFactory.Create(connectionString);

            var shared = await (
                from i in db.Items.AsNoTracking()
                where i.PlacementEligible && i.LifecycleStatus == "active"
                join v in db.ItemVersions.AsNoTracking() on i.ItemId equals v.ItemId
                group new { i.Modality, v.Stem } by new { i.Modality, v.Stem } into g
                where g.Count() > 1
                select new { g.Key.Modality, g.Key.Stem, Count = g.Count() })
                .ToListAsync();

            Assert.True(shared.Count == 0,
                "these stems are shared by more than one eligible item, so the instrument measures recall "
                + "rather than level:" + Environment.NewLine
                + string.Join(Environment.NewLine, shared.Select(s => $"  [{s.Modality}] x{s.Count}: {s.Stem[..Math.Min(70, s.Stem.Length)]}")));
        }
        finally
        {
            await database.DropDatabaseAsync(name);
        }
    }
}
