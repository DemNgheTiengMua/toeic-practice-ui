using System;
using System.Collections.Generic;
using System.Linq;

namespace BusinessObjects;

/// <summary>
/// #215 / D7, PLC-26: stage two is the <b>boundary stage</b>. It measures the pair of bands stage one
/// routed to, and every routed band is evaluated on the same number of items so the 70% pass rule is not
/// systematically unfair at one band.
/// <para>
/// <b>Why seven and not "what stage one already asked for".</b> Converting "≥70% correct at band L and
/// at every evaluated lower band" into a discrete rule is what makes the passing bar land off the
/// cutoff. At n=5 the bar is 4/5 = 80% (26.27% false demotion of true masters); at n=6 it is
/// 5/6 = 83.3% (34.46%); at n=7 it is 5/7 = 71.43%, which sits on the 70% cutoff. Stage one contributes
/// 1 item at A1 and 2 at B1, so the delivery here is 7 minus the stage-one count at that band - six at
/// the outside bands, five at the middle. The criterion is equality <i>across bands</i>, never equality
/// across stages: those are different sentences and only the first is true.
/// </para>
/// <para>
/// <b>Why the pair is routed, not searched.</b> Every item in the pool is addressed by (skill, level,
/// topic), so band L is a real bucket to draw from. The route is a pure function of one integer, which is
/// what makes it testable without a database and identical for two learners with the same score.
/// </para>
/// <para>
/// <b>Why the draw is here rather than in the service.</b> The cascade - preferred topic, then another
/// topic at the same band, then the practice reserve at the same band, then the oldest-seen item at that
/// band - is arithmetic over a list of candidates. Keeping it out of the query is what lets each tier be
/// driven by its own stock instead of by whatever the database happened to hold.
/// </para>
/// </summary>
public static class PlacementStageTwoBlueprint
{
    /// <summary>
    /// D7: "every routed band ends with exactly 7 evaluated items (Stage 1 contribution + Stage 2
    /// delivery)". The equalised count the 70% rule is derived against.
    /// </summary>
    public const int EvaluatedItemsPerBand = 7;

    /// <summary>
    /// D10's floor: below five stage-two items at a routed band, one lucky guess on a four-option item is
    /// 25 percentage points, so the band cannot be measured. A band that cannot reach it refuses the
    /// sitting rather than running a paper the rule cannot read.
    /// </summary>
    public const int MinimumStageTwoItemsAtABand = 5;

    /// <summary>
    /// The five rows of D7's routing table, ascending. Each row owns a contiguous run of stage-one
    /// scores and delivers <c>EvaluatedItemsPerBand - stage-one count</c> at each of its two bands.
    /// </summary>
    public static IReadOnlyList<PlacementStageTwoRoute> Routes { get; } =
    [
        new(0, 2, CefrLevel.A1, CefrLevel.A2),
        new(3, 3, CefrLevel.A2, CefrLevel.B1),
        new(4, 5, CefrLevel.B1, CefrLevel.B2),
        new(6, 6, CefrLevel.B2, CefrLevel.C1),
        new(7, 8, CefrLevel.C1, CefrLevel.C2),
    ];

    /// <summary>
    /// The routed pair for a stage-one score. Pure: the score is the only input, so two learners on the
    /// same score sit the same pair.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A score the stage-one blueprint cannot produce is a caller bug, not a low candidate; rounding it
    /// to the nearest pair would silently serve the wrong boundary.
    /// </exception>
    public static PlacementStageTwoRoute Route(int stageOneScore)
    {
        foreach (var route in Routes)
        {
            if (stageOneScore >= route.FromScore && stageOneScore <= route.ToScore)
            {
                return route;
            }
        }

        throw new ArgumentOutOfRangeException(
            nameof(stageOneScore),
            stageOneScore,
            $"A stage-one score runs from 0 to {PlacementStageOneBlueprint.ItemsPerObjectiveSkill}; there is no route for this one.");
    }

    /// <summary>
    /// Draws the stage-two paper for one objective skill against the stock the caller supplies.
    /// <para>
    /// <b>The cascade, in order, and never across a band.</b> For each band of the routed pair: unseen
    /// placement-eligible items from the preferred topic; then unseen placement-eligible items from any
    /// other topic at the same band; then unseen practice-reserve items at the same band; then seen items
    /// at the same band, oldest first. An adjacent band is never a source, because taking one would
    /// change the denominator the 70% rule is computed over.
    /// </para>
    /// </summary>
    /// <param name="stock">Every item the caller may use, each with its band, topic and history.</param>
    /// <param name="route">The pair the stage-one score routed to.</param>
    /// <param name="preferredTopicId">
    /// The topic the sitting would rather draw from, if any. A null preference means the caller has none,
    /// so every eligible topic is a primary source rather than a relaxation.
    /// </param>
    public static PlacementStageTwoSelection Select(
        IEnumerable<PlacementStageTwoStock> stock,
        PlacementStageTwoRoute route,
        int? preferredTopicId = null)
    {
        ArgumentNullException.ThrowIfNull(stock);
        ArgumentNullException.ThrowIfNull(route);

        var available = stock.ToList();
        var items = new List<PlacementStageTwoItem>();
        var bands = new List<PlacementStageTwoBandCount>();

        foreach (var (level, wanted) in route.Delivery)
        {
            var atBand = available.Where(candidate => candidate.Level == level).ToList();
            var taken = new List<PlacementStageTwoItem>(wanted);

            // A drawn item is never drawn twice, and a caller that listed one twice is still asking for
            // one: the item is the unit, not the row.
            bool Free(PlacementStageTwoStock candidate) =>
                taken.All(served => served.ItemId != candidate.ItemId);

            void Take(IEnumerable<PlacementStageTwoStock> from, PlacementStageTwoTier tier, int count)
            {
                foreach (var candidate in from.Where(Free).Take(count))
                {
                    taken.Add(new PlacementStageTwoItem(
                        candidate.ItemId, level, candidate.StimulusId, candidate.TopicId, tier));
                }
            }

            int Remaining() => wanted - taken.Count;

            // 1. The preferred topic's own cell.
            if (preferredTopicId is int topicId)
            {
                Take(atBand.Where(c => c.PlacementEligible && c.TimesSeen == 0 && c.TopicId == topicId),
                    PlacementStageTwoTier.Primary, Remaining());
            }
            else
            {
                Take(atBand.Where(c => c.PlacementEligible && c.TimesSeen == 0),
                    PlacementStageTwoTier.Primary, Remaining());
            }

            // 2. The same band, any other topic.
            Take(atBand.Where(c => c.PlacementEligible && c.TimesSeen == 0),
                PlacementStageTwoTier.RelaxedTopic, Remaining());

            // 3. The practice reserve, at this band. D7's second step is an UNSEEN reserve item: once the
            //    reserve has been answered too, the band is exhausted and recycling is the only move.
            Take(atBand.Where(c => !c.PlacementEligible && c.TimesSeen == 0),
                PlacementStageTwoTier.PracticeReserve, Remaining());

            // 4. Recycling: the oldest-seen placement item at this band. Oldest first, then lowest id, so
            //    the same stock always recycles the same rows. D7 restricts this step to
            //    placement_eligible = true, so a seen reserve item is not a recycling source.
            Take(atBand.Where(c => c.PlacementEligible)
                    .OrderBy(c => c.LastSeenAt ?? DateTimeOffset.MinValue).ThenBy(c => c.ItemId),
                PlacementStageTwoTier.RecycledOldestSeen, Remaining());

            items.AddRange(taken);
            bands.Add(new PlacementStageTwoBandCount(level, wanted, taken.Count));
        }

        return new PlacementStageTwoSelection(route, items, bands);
    }
}

/// <summary>
/// One row of D7's routing table: the stage-one scores it answers to, the pair it routes to, and how many
/// stage-two items each band of that pair is delivered.
/// </summary>
public sealed record PlacementStageTwoRoute(
    int FromScore,
    int ToScore,
    CefrLevel Lower,
    CefrLevel Upper)
{
    /// <summary>
    /// Stage two's share at each band of the pair, ascending. Derived, never stored: the delivery is
    /// "seven evaluated minus what stage one already asked", so a change to the stage-one blueprint
    /// cannot leave a second table disagreeing with it.
    /// </summary>
    public IReadOnlyList<(CefrLevel Level, int Count)> Delivery { get; } =
    [
        (Lower, PlacementStageTwoBlueprint.EvaluatedItemsPerBand - PlacementStageOneBlueprint.CountAt(Lower)),
        (Upper, PlacementStageTwoBlueprint.EvaluatedItemsPerBand - PlacementStageOneBlueprint.CountAt(Upper)),
    ];

    public int LowerDelivery => Delivery[0].Count;

    public int UpperDelivery => Delivery[1].Count;

    /// <summary>How many items stage two delivers across the pair: 12, 11, 10, 11, 12 by row.</summary>
    public int ItemCount => LowerDelivery + UpperDelivery;

    /// <summary>How many items this band ends with once stage one and stage two are both counted.</summary>
    public int EvaluatedAt(CefrLevel level) =>
        PlacementStageOneBlueprint.CountAt(level) + Delivery.Where(band => band.Level == level).Sum(band => band.Count);

    /// <summary>The pair as one stable token, for a log line or a stored routing decision (<c>A1_A2</c>).</summary>
    public string Code => $"{Lower.Code}_{Upper.Code}";
}

/// <summary>Which tier of D7's cascade supplied an item. Carried so a plan can show why a paper is shaped
/// the way it is, and so a test can tell the tiers apart rather than reading a count that looks the same.</summary>
public enum PlacementStageTwoTier
{
    /// <summary>Unseen, placement-eligible, in the preferred topic's own cell.</summary>
    Primary,

    /// <summary>Unseen and placement-eligible, but from another topic at the same band (tier 1).</summary>
    RelaxedTopic,

    /// <summary>Unseen practice stock at the same band (tier 2).</summary>
    PracticeReserve,

    /// <summary>Already seen, recycled because the band holds nothing else (tier 3).</summary>
    RecycledOldestSeen,
}

/// <summary>
/// One candidate the stage-two draw may use. A candidate is an item, not a bundle: two children of one
/// stimulus are two candidates, which is what makes a six-item draw from three- and four-child stimuli
/// possible at all (D7).
/// </summary>
/// <param name="ItemId">The item.</param>
/// <param name="Level">The band the item is authored at. A draw never leaves this band.</param>
/// <param name="TopicId">The item's canonical topic, used only by tier 1 of the cascade.</param>
/// <param name="StimulusId">The stimulus it shares, if any. Carried so a partial bundle can be proved.</param>
/// <param name="PlacementEligible">
/// False marks practice reserve stock: usable when the band's placement stock is exhausted, never before.
/// </param>
/// <param name="TimesSeen">How many times this learner has answered it. Zero means unseen.</param>
/// <param name="LastSeenAt">When last answered, for the oldest-first recycle.</param>
public sealed record PlacementStageTwoStock(
    int ItemId,
    CefrLevel Level,
    int TopicId,
    int? StimulusId = null,
    bool PlacementEligible = true,
    int TimesSeen = 0,
    DateTimeOffset? LastSeenAt = null);

/// <summary>One item a stage-two draw chose: which item, which band, and which tier supplied it.</summary>
public sealed record PlacementStageTwoItem(
    int ItemId,
    CefrLevel Level,
    int? StimulusId,
    int TopicId,
    PlacementStageTwoTier Tier);

/// <summary>One band's share of a stage-two draw: asked for, and supplied.</summary>
public sealed record PlacementStageTwoBandCount(CefrLevel Level, int Wanted, int Drawn)
{
    public int Missing => Math.Max(0, Wanted - Drawn);
}

/// <summary>
/// A stage-two draw's outcome.
/// <para>
/// Three outcomes, deliberately distinct. <b>Complete</b>: every band supplied its full count.
/// <b>Degraded</b>: a band came up short but still reached <see cref="PlacementStageTwoBlueprint.MinimumStageTwoItemsAtABand"/>,
/// so the paper runs and records the shortfall. <b>Refused</b>: a band could not reach that floor, so the
/// sitting must not start - the caller refuses before any attempt row exists, naming the cells.
/// </para>
/// </summary>
public sealed record PlacementStageTwoSelection(
    PlacementStageTwoRoute Route,
    IReadOnlyList<PlacementStageTwoItem> Items,
    IReadOnlyList<PlacementStageTwoBandCount> Bands)
{
    public int ItemCount => Items.Count;

    /// <summary>How many items the draw had to recycle because every item at their band had been seen.</summary>
    public int RecycledCount => Items.Count(item => item.Tier == PlacementStageTwoTier.RecycledOldestSeen);

    /// <summary>True when every band supplied its full count.</summary>
    public bool IsComplete => Bands.All(band => band.Drawn >= band.Wanted);

    /// <summary>The bands that supplied less than their full count, ascending.</summary>
    public IReadOnlyList<PlacementStageTwoBandCount> Deficits =>
        Bands.Where(band => band.Drawn < band.Wanted).ToList();

    /// <summary>The bands below D10's floor, which make the sitting unsittable rather than merely shorter.</summary>
    public IReadOnlyList<PlacementStageTwoBandCount> RefusedBands =>
        Bands.Where(band => band.Drawn < PlacementStageTwoBlueprint.MinimumStageTwoItemsAtABand).ToList();

    /// <summary>True when a band cannot be measured and the sitting must not start.</summary>
    public bool IsRefused => RefusedBands.Count > 0;

    /// <summary>True when the paper runs with fewer items than a full form, and must record that.</summary>
    public bool IsDegraded => !IsRefused && Deficits.Count > 0;
}
