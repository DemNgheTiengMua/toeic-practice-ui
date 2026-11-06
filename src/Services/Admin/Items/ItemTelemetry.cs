using System;
using System.Collections.Generic;
using System.Linq;
using BusinessObjects;

namespace Services;

/// <summary>
/// #199 / D0.7 / CNT-44: one administration of one item, as the statistics need it.
/// <para>
/// <see cref="TestTotal"/> is the total the learner scored in the same sitting, which is what the
/// point-biserial correlation needs: the correlation is between <em>this item's</em> 0/1 score and the
/// <em>same learner's whole-test</em> score. A "this item was answered wrongly" count is not that
/// quantity and would quarantine good items.
/// </para>
/// <para>
/// <see cref="TargetBand"/> is the caller's answer to "is this learner at the item's own level"; it is
/// decided outside this type because the evidence for it comes from the learner's measured skill state.
/// <see cref="ResponseBand"/> is a productive task's AI band (0 = Pre-A1 … 6 = C2, D0.6) and is null for
/// an objective item, which has a key rather than a band.
/// </para>
/// </summary>
public readonly record struct ItemObservation(
    string? SelectedLabel,
    bool? IsCorrect,
    double TestTotal,
    bool TargetBand,
    double? ResponseBand);

/// <summary>
/// #199 / D0.7 / CNT-44: the four telemetry quarantine conditions, as one pure function.
/// <para>
/// Nothing here reads a counter. Every number is derived from the stored answers handed in, so a
/// recount cannot disagree with itself — the defect NFI-05 forbids. This type holds the arithmetic
/// only; <see cref="ItemTelemetryService"/> decides what to hand it and what to do with the verdict.
/// </para>
/// <para>
/// Administered at <b>30, 50, 100 and 250</b>. Below 30 nothing is judged: a correlation over one item
/// is undefined and a facility over three answers is noise, and a gate that fired on either is exactly
/// the "threshold that cannot fail" trap — it would look right and quarantine healthy stock.
/// </para>
/// </summary>
public static class ItemTelemetry
{
    public const string BreachDiscrimination = "discrimination";
    public const string BreachFacility = "facility";
    public const string BreachDistractorDominance = "distractor_dominance";
    public const string BreachProductiveDrift = "productive_drift";

    /// <summary>D0.7: the checkpoints. A checkpoint is evaluated, never skipped; the thresholds below
    /// are what change with the sample size.</summary>
    public static IReadOnlyList<int> Checkpoints { get; } = [30, 50, 100, 250];

    /// <summary>D0.7 condition 1: below 0.10 at N ≥ 50, below 0.00 at N ≥ 30.</summary>
    public const double DiscriminationFloorAtFifty = 0.10;

    /// <summary>D0.7 condition 2: the usable facility band, closed at both ends.</summary>
    public const double FacilityFloor = 0.30;
    public const double FacilityCeiling = 0.92;

    /// <summary>D0.7 condition 3: "more than 45%" of target-band learners on one distractor, with the
    /// key "below 35%". Both are strict.</summary>
    public const double DistractorDominanceShare = 0.45;
    public const double KeyFloorShare = 0.35;

    /// <summary>D0.7 condition 4: more than three quarters of a band away from the item's level.</summary>
    public const double ProductiveDriftBands = 0.75;

    /// <summary>The sample size below which nothing is judged at all (D0.7's first checkpoint).</summary>
    public const int MinimumAdministrations = 30;

    /// <summary>The sample size from which the mid-band discrimination floor and the facility band
    /// apply.</summary>
    public const int DiscriminationAndFacilitySample = 50;

    /// <summary>
    /// The point-biserial correlation between item score and total test score — a Pearson correlation,
    /// computed from the observations rather than from a maintained counter.
    /// <para>
    /// Returns null where the coefficient is <b>undefined</b>: fewer than two observations, or no
    /// variance in either variable (an item everyone answered correctly, a sitting where every total is
    /// the same). That is the trap this signature exists to make unmissable — zero would fire the
    /// <c>&lt; 0.10</c> gate on a perfectly discriminating item, and one would hide a broken one.
    /// </para>
    /// </summary>
    public static double? PointBiserial(IReadOnlyCollection<(double Item, double Total)> observations)
    {
        if (observations.Count < 2)
        {
            return null;
        }

        var n = observations.Count;
        var itemMean = observations.Average(o => o.Item);
        var totalMean = observations.Average(o => o.Total);

        // Population standard deviations, matching the classical M1/M0 form the test asserts against.
        var itemSd = Math.Sqrt(observations.Sum(o => (o.Item - itemMean) * (o.Item - itemMean)) / n);
        var totalSd = Math.Sqrt(observations.Sum(o => (o.Total - totalMean) * (o.Total - totalMean)) / n);

        if (itemSd <= double.Epsilon || totalSd <= double.Epsilon)
        {
            return null;
        }

        var covariance = observations.Sum(o => (o.Item - itemMean) * (o.Total - totalMean)) / n;
        return covariance / (itemSd * totalSd);
    }

    /// <summary>
    /// Evaluates the four D0.7 conditions over one item's stored answers.
    /// <para>
    /// The breach set is returned in full and by name, <b>not</b> collapsed to a boolean: "there is a
    /// breach" cannot tell a broken key from a drifting rubric, and a test that asserted only the
    /// boolean would pass while every condition fired on the wrong input. Each rule also carries its own
    /// sample-size gate, so a condition cannot fire on a sample it does not apply to.
    /// </para>
    /// </summary>
    public static ItemTelemetryVerdict Evaluate(
        CefrLevel itemLevel,
        string? keyLabel,
        IReadOnlyCollection<ItemObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        var administered = observations
            .Where(o => o.IsCorrect is not null || o.ResponseBand is not null)
            .ToList();

        var targetBand = administered.Where(o => o.TargetBand).ToList();
        // Facility is an objective quantity: a productive response has no correct/incorrect, and
        // averaging its null as 0 would report every productive task at 0% facility.
        var targetObjective = targetBand.Where(o => o.IsCorrect is not null).ToList();
        var targetFacility = targetObjective.Count == 0
            ? (double?)null
            : targetObjective.Average(o => o.IsCorrect == true ? 1d : 0d);

        var discrimination = PointBiserial(administered
            .Where(o => o.IsCorrect is not null)
            .Select(o => (o.IsCorrect == true ? 1d : 0d, o.TestTotal))
            .ToList());

        var bandedResponses = administered.Where(o => o.ResponseBand is not null).ToList();
        var meanResponseBand = bandedResponses.Count == 0
            ? (double?)null
            : bandedResponses.Average(o => o.ResponseBand!.Value);

        var breaches = new List<string>();
        var administeredCount = administered.Count;

        // Nothing is judged below the first checkpoint. Returning early is the point: every threshold
        // below assumes a sample it would be wrong to read on three answers.
        if (administeredCount < MinimumAdministrations)
        {
            return new ItemTelemetryVerdict(
                itemLevel,
                administeredCount,
                targetBand.Count,
                Evaluated: false,
                discrimination,
                targetFacility,
                meanResponseBand,
                breaches);
        }

        // 1. point-biserial discrimination.
        if (discrimination is { } r)
        {
            var floor = administeredCount >= DiscriminationAndFacilitySample
                ? DiscriminationFloorAtFifty
                : 0d;
            if (r < floor)
            {
                breaches.Add(BreachDiscrimination);
            }
        }

        // 2. target-band facility, only from the 50 checkpoint.
        if (administeredCount >= DiscriminationAndFacilitySample
            && targetFacility is { } facility
            && (facility < FacilityFloor || facility > FacilityCeiling))
        {
            breaches.Add(BreachFacility);
        }

        // 3. one distractor outswallowing the key. The worst share is computed over the DISTRACTORS:
        // including the key would report a dominant key as a dominant wrong answer, and every
        // well-behaved item has a dominant key.
        if (keyLabel is not null && targetObjective.Count > 0 && targetFacility is { } keyShare)
        {
            var worstDistractor = targetObjective
                .Where(o => !string.Equals(o.SelectedLabel, keyLabel, StringComparison.OrdinalIgnoreCase))
                .GroupBy(o => o.SelectedLabel ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(g => (double)g.Count() / targetObjective.Count)
                .DefaultIfEmpty(0d)
                .Max();

            if (worstDistractor > DistractorDominanceShare && keyShare < KeyFloorShare)
            {
                breaches.Add(BreachDistractorDominance);
            }
        }

        // 4. a productive task's AI band drifting away from its authored level.
        if (bandedResponses.Count >= MinimumAdministrations
            && meanResponseBand is { } mean
            && Math.Abs(mean - itemLevel.Rank) > ProductiveDriftBands)
        {
            breaches.Add(BreachProductiveDrift);
        }

        return new ItemTelemetryVerdict(
            itemLevel,
            administeredCount,
            targetBand.Count,
            Evaluated: true,
            discrimination,
            targetFacility,
            meanResponseBand,
            breaches);
    }

    /// <summary>
    /// Which checkpoint this administration count has <em>reached most recently</em>, or null below 30.
    /// The scheduled job evaluates an item when its count crosses one of these, and the count is a
    /// property of the stored answers, so re-running the job cannot advance it.
    /// </summary>
    public static int? ReachedCheckpoint(int administrations)
    {
        int? reached = null;
        foreach (var checkpoint in Checkpoints)
        {
            if (administrations >= checkpoint)
            {
                reached = checkpoint;
            }
        }

        return reached;
    }
}

/// <summary>
/// #199: what one item's stored answers say. Every field is a derived number, kept beside the breach
/// set so a reviewer can see <em>why</em> an item was quarantined rather than only that it was.
/// </summary>
public sealed record ItemTelemetryVerdict(
    CefrLevel ItemLevel,
    int Administrations,
    int TargetBandAdministrations,
    bool Evaluated,
    double? Discrimination,
    double? TargetFacility,
    double? MeanResponseBand,
    IReadOnlyList<string> Breaches)
{
    /// <summary>True when at least one D0.7 condition is met: the item becomes <c>quarantined</c>.</summary>
    public bool ShouldQuarantine => Breaches.Count > 0;
}
