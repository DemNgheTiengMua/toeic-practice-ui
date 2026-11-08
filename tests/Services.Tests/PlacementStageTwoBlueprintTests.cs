using System;
using System.Collections.Generic;
using System.Linq;
using BusinessObjects;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #215 / D7: stage two is the boundary stage, as arithmetic.
/// <para>
/// The routing table is five rows and every one is asserted literally, because the criterion is "the
/// routed pair is a pure function of the stage-one score" and a table tested only by its first row
/// passes for a router that answers A1-A2 to everyone.
/// </para>
/// <para>
/// <b>The trap this file is written against:</b> "every routed band is evaluated on the same number of
/// items" implemented as "the same number as stage one". Those are different sentences. The assertion is
/// equality <i>across bands</i> (7 at both bands of every pair), never across stages - stage one asks 1
/// at A1 and 2 at B1, so a stage-two delivery equal to the stage-one count would give 1/1 and 2/2.
/// </para>
/// <para>
/// The cascade is three separate tiers - topic relaxation, the practice reserve, and the refusal of an
/// adjacent band - and each gets its own test with its own stock. One fixture where everything succeeds
/// would pass all three behaviours at once and prove none of them.
/// </para>
/// </summary>
public sealed class PlacementStageTwoBlueprintTests
{
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static PlacementStageTwoStock Stock(
        int itemId,
        CefrLevel level,
        int topicId,
        bool placementEligible = true,
        int timesSeen = 0,
        int? stimulusId = null,
        int? lastSeenDaysAgo = null) =>
        new(
            itemId,
            level,
            topicId,
            stimulusId,
            placementEligible,
            timesSeen,
            lastSeenDaysAgo is int days ? Origin.AddDays(days) : null);

    private static List<PlacementStageTwoStock> StockAt(int firstId, int count, CefrLevel level, int topicId) =>
        Enumerable.Range(firstId, count).Select(id => Stock(id, level, topicId)).ToList();

    /// <summary>
    /// The five rows of D7's table: (0-2 → A1-A2, 6+6), (3 → A2-B1, 6+5), (4-5 → B1-B2, 5+5),
    /// (6 → B2-C1, 5+6), (7-8 → C1-C2, 6+6).
    ///
    /// <b>The input that would pass a weaker test:</b> a router keyed on "score ÷ 2" returns a plausible
    /// adjacent pair for every score and gets the boundaries at 3, 6 and 7 wrong. Every score from 0 to 8
    /// is mapped here, so an off-by-one is a named failure rather than a silently different paper.
    /// </summary>
    [Fact]
    public void The_routing_table_is_the_five_rows_the_spec_states()
    {
        var expected = new (CefrLevel Lower, CefrLevel Upper, int LowerDelivery, int UpperDelivery, int[] Scores)[]
        {
            (CefrLevel.A1, CefrLevel.A2, 6, 6, [0, 1, 2]),
            (CefrLevel.A2, CefrLevel.B1, 6, 5, [3]),
            (CefrLevel.B1, CefrLevel.B2, 5, 5, [4, 5]),
            (CefrLevel.B2, CefrLevel.C1, 5, 6, [6]),
            (CefrLevel.C1, CefrLevel.C2, 6, 6, [7, 8]),
        };

        Assert.Equal(5, PlacementStageTwoBlueprint.Routes.Count);

        foreach (var row in expected)
        {
            foreach (var score in row.Scores)
            {
                var route = PlacementStageTwoBlueprint.Route(score);
                Assert.Equal(
                    (row.Lower, row.Upper, row.LowerDelivery, row.UpperDelivery),
                    (route.Lower, route.Upper, route.LowerDelivery, route.UpperDelivery));
            }
        }
    }

    /// <summary>
    /// D7: "every routed band ends with exactly 7 evaluated items (Stage 1 contribution + Stage 2
    /// delivery)". Both bands of all five rows are checked separately - a route whose lower band totals
    /// 7 and whose upper band totals 6 is the 83.3% bar the equalised blueprint exists to prevent.
    ///
    /// <b>The input that would pass a weaker test:</b> a stage-two delivery equal to the stage-one count
    /// at each band (1/1, 2/2, ...), which sums to 3 and 4 rather than 7 and 7.
    /// </summary>
    [Fact]
    public void Every_routed_band_is_evaluated_on_seven_items_at_both_bands_of_every_pair()
    {
        foreach (var route in PlacementStageTwoBlueprint.Routes)
        {
            foreach (var (level, delivery) in route.Delivery)
            {
                var stageOne = PlacementStageOneBlueprint.CountAt(level);
                Assert.Equal(PlacementStageTwoBlueprint.EvaluatedItemsPerBand, stageOne + delivery);
                Assert.Equal(PlacementStageTwoBlueprint.EvaluatedItemsPerBand, route.EvaluatedAt(level));
            }

            // The pair is adjacent. "Never an adjacent band" is a rule about substitution; a route that
            // skipped a band would make the rule unstateable.
            Assert.Equal(route.Lower.Rank + 1, route.Upper.Rank);
        }
    }

    /// <summary>
    /// Criterion 2, as the table states it: the <i>per-pair</i> total. Six plus six is twelve at the
    /// outside pairs, six plus five is eleven at the middle pairs, and five plus five is ten at B1-B2.
    /// A route that delivered seven everywhere would satisfy "seven evaluated per band" and get every one
    /// of these five totals wrong.
    /// </summary>
    [Fact]
    public void Each_pair_delivers_the_total_the_table_states()
    {
        var expected = new (string Code, int LowerDelivery, int UpperDelivery, int Total)[]
        {
            ("A1_A2", 6, 6, 12),
            ("A2_B1", 6, 5, 11),
            ("B1_B2", 5, 5, 10),
            ("B2_C1", 5, 6, 11),
            ("C1_C2", 6, 6, 12),
        };

        Assert.Equal(
            expected,
            PlacementStageTwoBlueprint.Routes
                .Select(route => (route.Code, route.LowerDelivery, route.UpperDelivery, route.ItemCount))
                .ToArray());
    }

    /// <summary>
    /// "The routed pair is a pure function of the stage-one score" is a claim about the <i>inputs</i>. The
    /// input that passes while being wrong is a router that also reads the learner, the certificate or the
    /// clock: it routes the same score differently for two learners, and any table test above still passes
    /// because it only ever asks one question at a time.
    ///
    /// Asserted structurally rather than by example, because an example cannot prove the absence of a
    /// second input: <see cref="PlacementStageTwoBlueprint.Route"/> is static, takes exactly one parameter,
    /// and its declaring type holds no state a call could depend on.
    /// </summary>
    [Fact]
    public void Routing_takes_the_score_and_nothing_else()
    {
        var type = typeof(PlacementStageTwoBlueprint);
        var route = type.GetMethod(nameof(PlacementStageTwoBlueprint.Route));

        Assert.NotNull(route);
        Assert.True(route!.IsStatic);
        Assert.Equal([typeof(int)], route.GetParameters().Select(parameter => parameter.ParameterType));
        // A static class cannot hold per-call state; no instance fields, and no mutable static field than
        // can be written after the table above is built.
        Assert.Empty(type.GetFields(System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic));
        Assert.DoesNotContain(
            type.GetFields(System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic),
            field => !field.IsInitOnly && !field.IsLiteral);
    }

    /// <summary>A score the stage-one blueprint cannot have produced is refused, not rounded to a pair.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void A_score_outside_the_stage_one_blueprint_is_refused(int score)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlacementStageTwoBlueprint.Route(score));
    }

    /// <summary>
    /// Tier one: the preferred topic at A1 holds three of the six asked for, and another topic at the
    /// same band has more. The draw relaxes <i>topic</i> and keeps the band.
    ///
    /// <b>The input that would pass a weaker test:</b> a selector with no topic preference at all - it
    /// draws six and never reveals that the preferred cell was short. The assertion counts the two tiers.
    /// </summary>
    [Fact]
    public void An_empty_preferred_cell_relaxes_the_topic_within_the_same_band()
    {
        var route = PlacementStageTwoBlueprint.Route(0);
        var stock = StockAt(1, 3, CefrLevel.A1, topicId: 1);
        stock.AddRange(StockAt(11, 5, CefrLevel.A1, topicId: 2));
        stock.AddRange(StockAt(21, 6, CefrLevel.A2, topicId: 1));

        var selection = PlacementStageTwoBlueprint.Select(stock, route, preferredTopicId: 1);

        var a1 = selection.Items.Where(item => item.Level == CefrLevel.A1).ToList();
        Assert.Equal(6, a1.Count);
        Assert.Equal(3, a1.Count(item => item.Tier == PlacementStageTwoTier.Primary));
        Assert.Equal(3, a1.Count(item => item.Tier == PlacementStageTwoTier.RelaxedTopic));
        Assert.All(a1, item => Assert.Equal(CefrLevel.A1, item.Level));
        Assert.True(selection.IsComplete);
    }

    /// <summary>
    /// Tier two, on its own stock: every placement-eligible A2 item has been seen, the practice reserve
    /// at A2 has not, so the draw reaches the reserve - at the same band.
    ///
    /// <b>The input that would pass a weaker test:</b> a selector that ignores <c>PlacementEligible</c>.
    /// It draws reserve items here and the counts look identical; only the tier label separates them.
    /// </summary>
    [Fact]
    public void An_exhausted_band_draws_from_the_practice_reserve_and_says_so()
    {
        var route = PlacementStageTwoBlueprint.Route(0);
        var stock = StockAt(1, 6, CefrLevel.A1, topicId: 1);
        stock.AddRange(Enumerable.Range(11, 6)
            .Select(id => Stock(id, CefrLevel.A2, 1, timesSeen: 1, lastSeenDaysAgo: 10)));
        stock.AddRange(Enumerable.Range(21, 6)
            .Select(id => Stock(id, CefrLevel.A2, 1, placementEligible: false)));

        var selection = PlacementStageTwoBlueprint.Select(stock, route);

        var a2 = selection.Items.Where(item => item.Level == CefrLevel.A2).ToList();
        Assert.Equal(6, a2.Count);
        Assert.All(a2, item => Assert.Equal(PlacementStageTwoTier.PracticeReserve, item.Tier));
        Assert.All(a2, item => Assert.InRange(item.ItemId, 21, 26));
        Assert.True(selection.IsComplete);
        Assert.False(selection.IsDegraded);
    }

    /// <summary>
    /// Tier three, on its own stock: every eligible A2 item has been seen and the reserve is empty, so the
    /// draw recycles seen items at that band and reports it.
    ///
    /// <b>The input that would pass a weaker test:</b> a selector that simply drops seen items. It reports
    /// a six-item deficit here and refuses a sitting the spec says may run.
    /// </summary>
    [Fact]
    public void A_band_with_only_seen_items_recycles_the_oldest_seen_at_that_band()
    {
        var route = PlacementStageTwoBlueprint.Route(0);
        var stock = StockAt(1, 6, CefrLevel.A1, topicId: 1);
        stock.AddRange(Enumerable.Range(11, 6)
            .Select(id => Stock(id, CefrLevel.A2, 1, timesSeen: 1, lastSeenDaysAgo: id)));

        var selection = PlacementStageTwoBlueprint.Select(stock, route);

        var a2 = selection.Items.Where(item => item.Level == CefrLevel.A2).ToList();
        Assert.Equal(6, a2.Count);
        Assert.All(a2, item => Assert.Equal(PlacementStageTwoTier.RecycledOldestSeen, item.Tier));
        Assert.Equal(6, selection.RecycledCount);
        Assert.True(selection.IsComplete);
    }

    /// <summary>
    /// The refusal, on its own stock: A1 holds nothing at all, while A2 is over-full. A selector that
    /// substituted the neighbouring band would assemble a complete, plausible paper.
    ///
    /// <b>The input that would pass a weaker test:</b> exactly that substitution - every count still sums
    /// to twelve and the deficit list is empty.
    /// </summary>
    [Fact]
    public void A_band_never_substitutes_an_adjacent_band()
    {
        var route = PlacementStageTwoBlueprint.Route(0);
        var stock = StockAt(1, 30, CefrLevel.A2, topicId: 1);

        var selection = PlacementStageTwoBlueprint.Select(stock, route);

        Assert.DoesNotContain(selection.Items, item => item.Level == CefrLevel.A1);
        Assert.Equal(6, selection.ItemCount);
        var missing = Assert.Single(selection.RefusedBands);
        Assert.Equal(CefrLevel.A1, missing.Level);
        Assert.Equal(6, missing.Missing);
        Assert.True(selection.IsRefused);
    }

    /// <summary>
    /// D7's partial-stimulus requirement at the counts stage two asks for: six items at A1 from one
    /// three-child stimulus and one four-child stimulus is 3 + 3, a <b>strict subset</b> of the second
    /// bundle. Neither the six-item draw nor the five-item one divides by three or four.
    ///
    /// <b>The input that would pass a weaker test:</b> a bundle-atomic selector serves three items (the
    /// first bundle only) or seven (both bundles whole). The strict-subset assertion is the one a
    /// bundle-atomic selector cannot satisfy.
    /// </summary>
    [Fact]
    public void A_six_item_band_serves_a_partial_subset_of_a_shared_stimulus()
    {
        const int ThreeChild = 900;
        const int FourChild = 901;
        var route = PlacementStageTwoBlueprint.Route(0);
        var stock = Enumerable.Range(1, 3)
            .Select(id => Stock(id, CefrLevel.A1, 1, stimulusId: ThreeChild)).ToList();
        stock.AddRange(Enumerable.Range(11, 4)
            .Select(id => Stock(id, CefrLevel.A1, 1, stimulusId: FourChild)));
        stock.AddRange(StockAt(21, 6, CefrLevel.A2, topicId: 1));

        var selection = PlacementStageTwoBlueprint.Select(stock, route);

        var a1 = selection.Items.Where(item => item.Level == CefrLevel.A1).ToList();
        Assert.Equal(6, a1.Count);
        Assert.Equal(6, a1.Select(item => item.ItemId).Distinct().Count());
        Assert.Equal(3, a1.Count(item => item.StimulusId == ThreeChild));
        // A strict subset: three of the four children, never the whole bundle.
        Assert.Equal(3, a1.Count(item => item.StimulusId == FourChild));
        Assert.True(selection.IsComplete);
    }

    /// <summary>
    /// The same requirement at the other stage-two count, from the other direction: a five-item band
    /// holding a four-child and a three-child bundle serves 4 + <b>1</b>. One child of a shared stimulus,
    /// with its two siblings left for another sitting.
    ///
    /// <b>The input that would pass a weaker test:</b> a bundle-atomic selector returns seven (both
    /// bundles whole) or four. Exactly-one-child is the assertion neither can satisfy.
    /// </summary>
    [Fact]
    public void A_five_item_band_names_exactly_one_child_of_a_shared_stimulus()
    {
        const int FourChild = 910;
        const int ThreeChild = 911;
        // B1-B2 asks 5 at each band, so score 4 routes there.
        var route = PlacementStageTwoBlueprint.Route(4);
        var stock = Enumerable.Range(1, 4)
            .Select(id => Stock(id, CefrLevel.B1, 1, stimulusId: FourChild)).ToList();
        stock.AddRange(Enumerable.Range(11, 3)
            .Select(id => Stock(id, CefrLevel.B1, 1, stimulusId: ThreeChild)));
        stock.AddRange(StockAt(21, 5, CefrLevel.B2, topicId: 1));

        var selection = PlacementStageTwoBlueprint.Select(stock, route);

        var b1 = selection.Items.Where(item => item.Level == CefrLevel.B1).ToList();
        Assert.Equal(5, b1.Count);
        Assert.Equal(4, b1.Count(item => item.StimulusId == FourChild));
        Assert.Single(b1, item => item.StimulusId == ThreeChild);
        Assert.True(selection.IsComplete);
    }

    /// <summary>
    /// D10's floor and the ticket's degraded form, on two stocks that differ by one item. Four usable
    /// items at a routed band cannot support the 70% rule (at four, one lucky guess is 25 points) so the
    /// sitting is refused; five can, so it runs and records the shortfall.
    ///
    /// <b>The input that would pass a weaker test:</b> four at A2 - the paper assembles, every count is at
    /// most six, and only the floor separates it from a form the spec forbids.
    /// </summary>
    [Fact]
    public void A_band_below_the_five_item_floor_refuses_while_five_of_six_runs_degraded()
    {
        var route = PlacementStageTwoBlueprint.Route(0);

        var fourAtA2 = StockAt(1, 6, CefrLevel.A1, topicId: 1);
        fourAtA2.AddRange(StockAt(11, 4, CefrLevel.A2, topicId: 1));
        Assert.True(PlacementStageTwoBlueprint.Select(fourAtA2, route).IsRefused);

        var fiveAtA2 = StockAt(1, 6, CefrLevel.A1, topicId: 1);
        fiveAtA2.AddRange(StockAt(11, 5, CefrLevel.A2, topicId: 1));
        var degraded = PlacementStageTwoBlueprint.Select(fiveAtA2, route);

        Assert.False(degraded.IsRefused);
        Assert.False(degraded.IsComplete);
        Assert.True(degraded.IsDegraded);
        var shortBand = Assert.Single(degraded.Deficits);
        Assert.Equal(CefrLevel.A2, shortBand.Level);
        Assert.Equal((6, 5), (shortBand.Wanted, shortBand.Drawn));
        // A degraded form serves fewer items. It never repeats one to reach the count.
        Assert.Equal(11, degraded.ItemCount);
        Assert.Equal(11, degraded.Items.Select(item => item.ItemId).Distinct().Count());
    }

    /// <summary>The same stock, drawn twice, is the same paper - a resume must not move it.</summary>
    [Fact]
    public void The_same_stock_draws_the_same_items_in_the_same_order()
    {
        var route = PlacementStageTwoBlueprint.Route(4);
        var stock = Enumerable.Range(1, 8).Select(id => Stock(id, CefrLevel.B1, id % 3)).ToList();
        stock.AddRange(Enumerable.Range(20, 8).Select(id => Stock(id, CefrLevel.B2, id % 3)));

        var first = PlacementStageTwoBlueprint.Select(stock, route);
        var again = PlacementStageTwoBlueprint.Select(stock, route);

        Assert.Equal(first.Items.Select(item => item.ItemId), again.Items.Select(item => item.ItemId));
    }
}
