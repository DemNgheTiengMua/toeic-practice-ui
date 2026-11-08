using System;
using System.Collections.Generic;
using System.Linq;

namespace BusinessObjects;

/// <summary>
/// #214 / D7, PLC-22: stage one is a <b>fixed</b> blueprint, not a sampled one - 8 items per objective
/// skill spread over all six bands, one of A1 and C2 and two of the middle.
/// <para>
/// <b>Why the ends are not optional.</b> A blueprint that starts at A2 cannot route a beginner and one
/// that stops at B2 cannot route an advanced candidate; the two-stage design would then be a design for
/// the middle only. The two ends carry one item each precisely because a stage-one draw is a routing
/// instrument, not a measurement: stage two is what buys precision at the routed boundary (#215).
/// </para>
/// <para>
/// <b>Everything numeric about stage one lives here.</b> The distribution is a single table so the
/// selector, the deficit report and the tests cannot disagree about it, and there is no second place
/// to edit when the count changes.
/// </para>
/// </summary>
public static class PlacementStageOneBlueprint
{
    /// <summary>
    /// The fixed stage-one distribution, ascending by level. A1 and C2 get one item each; A2-C1 get two.
    /// Sums to <see cref="ItemsPerObjectiveSkill"/>.
    /// </summary>
    public static IReadOnlyList<(CefrLevel Level, int Count)> Distribution { get; } =
    [
        (CefrLevel.A1, 1),
        (CefrLevel.A2, 1),
        (CefrLevel.B1, 2),
        (CefrLevel.B2, 2),
        (CefrLevel.C1, 1),
        (CefrLevel.C2, 1),
    ];

    /// <summary>PLC-22: eight items per objective skill, every band represented.</summary>
    public static int ItemsPerObjectiveSkill => Distribution.Sum(band => band.Count);

    /// <summary>
    /// The only modalities stage one draws for. Speaking and writing are productive: their task is
    /// anchored on the objective half and routed by rubric (#219, #217), so a per-band item draw there
    /// would be a different measurement.
    /// </summary>
    public static IReadOnlyList<Modality> ObjectiveModalities { get; } = [Modality.Listening, Modality.Reading];

    /// <summary>The count the blueprint asks for at one band. Zero for the reportable floor.</summary>
    public static int CountAt(CefrLevel level) =>
        Distribution.FirstOrDefault(band => band.Level == level).Count;

    /// <summary>
    /// Draws one skill's stage one from the candidates it is given.
    /// <para>
    /// <b>A candidate is an item, not a bundle.</b> Two children of one stimulus are two candidates and
    /// are chosen separately, so asking for one item at a band can be answered with one of a shared
    /// stimulus's three children. That is the property the sitting needs: an intact-bundle rule would
    /// make a one-item draw impossible at every band whose content arrives in groups of three or four.
    /// </para>
    /// <para>
    /// <b>A band never borrows from its neighbour.</b> When a band is short the draw returns fewer items
    /// at that band and reports the shortfall by name; taking an A2 item to fill the C2 slot would
    /// change the denominator the 70% rule is computed over (PLC-26). The refusal that follows a
    /// deficit belongs to the caller (#215).
    /// </para>
    /// <para>
    /// The order is stable: bands ascending, and within a band the candidate order the caller supplied.
    /// A draw that reshuffled between two reads would move a candidate's paper under them on reload.
    /// </para>
    /// </summary>
    /// <param name="candidates">Every item the caller may use for this skill, each with its own band.</param>
    /// <param name="perBand">How many to take at each band; defaults to <see cref="Distribution"/>.</param>
    public static PlacementStageOneSelection Select(
        IEnumerable<PlacementStageOneCandidate> candidates,
        IReadOnlyList<(CefrLevel Level, int Count)>? perBand = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var wanted = perBand ?? Distribution;

        var byLevel = candidates
            .Where(candidate => !candidate.Level.IsReportableFloor)
            .GroupBy(candidate => candidate.Level)
            .ToDictionary(group => group.Key, group => group.ToList());

        var drawn = new List<PlacementStageOneCandidate>(ItemsPerObjectiveSkill);
        var counts = new List<PlacementStageOneBandCount>(wanted.Count);

        foreach (var (level, count) in wanted.OrderBy(band => band.Level.Rank))
        {
            var available = byLevel.TryGetValue(level, out var bucket) ? bucket : [];
            // An item already served is never served twice, and a caller that listed one twice is
            // still asking for one: the item is the unit of a draw, not the row.
            var taken = available
                .Where(candidate => drawn.All(served => served.ItemId != candidate.ItemId))
                .DistinctBy(candidate => candidate.ItemId)
                .Take(count)
                .ToList();
            drawn.AddRange(taken);
            counts.Add(new PlacementStageOneBandCount(level, count, taken.Count));
        }

        return new PlacementStageOneSelection(drawn, counts);
    }

    /// <summary>Whether a band the certificate examines is one stage one can draw at all.</summary>
    public static bool IsObjective(Modality modality) => ObjectiveModalities.Contains(modality);
}

/// <summary>
/// One item the draw may choose: its band, the item it is, and the stimulus it shares (if any). The
/// stimulus is carried only so a caller can prove a partial bundle was drawn; the selection itself
/// never treats children as a unit.
/// </summary>
public sealed record PlacementStageOneCandidate(
    int ItemId,
    CefrLevel Level,
    int? StimulusId = null,
    int? Position = null);

/// <summary>
/// A draw's outcome: the items chosen, and per band how many were asked for and how many existed.
/// A band whose <see cref="PlacementStageOneBandCount.Drawn"/> is below its <see cref="PlacementStageOneBandCount.Wanted"/>
/// is a deficit the caller must name rather than paper over.
/// </summary>
public sealed record PlacementStageOneSelection(
    IReadOnlyList<PlacementStageOneCandidate> Items,
    IReadOnlyList<PlacementStageOneBandCount> Bands)
{
    public int ItemCount => Items.Count;

    /// <summary>True when every band supplied its full count.</summary>
    public bool IsComplete => Bands.All(band => band.Drawn >= band.Wanted);

    /// <summary>The bands that could not supply what the blueprint asks for, ascending.</summary>
    public IReadOnlyList<PlacementStageOneBandCount> Deficits =>
        Bands.Where(band => band.Drawn < band.Wanted).ToList();
}

/// <summary>One band's share of a draw: asked for, and supplied.</summary>
public sealed record PlacementStageOneBandCount(CefrLevel Level, int Wanted, int Drawn)
{
    public int Missing => Math.Max(0, Wanted - Drawn);
}
