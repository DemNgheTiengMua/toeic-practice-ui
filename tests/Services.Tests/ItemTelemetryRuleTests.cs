using System;
using System.Collections.Generic;
using System.Linq;
using BusinessObjects;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #199 / D0.7 / CNT-44: the four quarantine conditions, as arithmetic over stored answers.
/// <para>
/// Every test here states the input that would pass each guard while being wrong, because a
/// statistics gate has two classic traps: a threshold that cannot fail (<c>&gt;= 0</c>, or a
/// correlation over one observation where it is undefined), and a gate that fires on the wrong
/// condition. So each condition is asserted <b>by name</b> — the breach set is compared exactly,
/// never "there is at least one breach" — and each condition gets a fixture that is bad on a
/// different one and fine on this one.
/// </para>
/// <para>
/// The pure rules live in <see cref="ItemTelemetry"/> — no database, no clock — so the boundaries can
/// be walked exactly. The half only a service can show (a flip to quarantined, selection excluding
/// it, the recalculation) is in <c>ItemTelemetryServiceTests</c>.
/// </para>
/// </summary>
public sealed class ItemTelemetryRuleTests
{
    /// <summary>
    /// A point-biserial correlation is the Pearson correlation between item score (0/1) and the total
    /// the same sitting produced. It is checked here against the <em>other</em> published form of the
    /// same statistic — <c>r = ((M1 - M0) / S_n) * sqrt(p·q)</c> — computed independently in the test.
    /// Two implementations of one quantity agreeing is evidence; one implementation compared against
    /// itself is not, which is the tautology this assertion exists to avoid.
    /// </summary>
    [Fact]
    public void Point_biserial_agrees_with_the_classical_M1_M0_form()
    {
        // 12 administrations, 7 correct. The two groups' totals overlap slightly (a wrong answer with
        // a total of 8 above a right answer with a total of 6), so a formula that only detected perfect
        // separation would fail this fixture rather than pass it.
        double[] totals = [9, 8, 7, 8, 6, 9, 2, 4, 8, 3, 5, 4];
        bool[] correct = [true, true, true, true, true, true, false, false, false, false, false, false];

        var pairs = totals.Select((t, i) => (Item: correct[i] ? 1d : 0d, Total: t)).ToList();
        var r = ItemTelemetry.PointBiserial(pairs);

        // the classical form, computed here from the same observations
        var n = totals.Length;
        var p = (double)correct.Count(c => c) / n;
        var q = 1 - p;
        var mean1 = totals.Where((_, i) => correct[i]).Average();
        var mean0 = totals.Where((_, i) => !correct[i]).Average();
        var mean = totals.Average();
        var sd = Math.Sqrt(totals.Sum(t => (t - mean) * (t - mean)) / n);
        var expected = (mean1 - mean0) / sd * Math.Sqrt(p * q);

        Assert.NotNull(r);
        Assert.Equal(expected, r!.Value, 10);
        // and the sign is the one the fixture intends: the item separates the two groups
        Assert.True(r.Value > 0.3, $"expected clear positive discrimination, got {r.Value}");
    }

    /// <summary>
    /// The trap this catches: a correlation computed over a sample where it is undefined. An item every
    /// learner answered correctly has zero variance in the item score, so the coefficient does not
    /// exist. Returning 0 (or NaN) would make a <c>&lt; 0.10</c> gate fire on a perfectly good item, and
    /// returning 1 would hide it. It is reported as absent, and the facility condition is what catches
    /// an over-easy item.
    /// </summary>
    [Fact]
    public void Point_biserial_is_absent_rather_than_zero_when_the_item_score_has_no_variance()
    {
        var allCorrect = Enumerable.Range(0, 40).Select(i => (Item: 1d, Total: (double)(i % 7))).ToList();
        Assert.Null(ItemTelemetry.PointBiserial(allCorrect));

        var allWrong = Enumerable.Range(0, 40).Select(i => (Item: 0d, Total: (double)(i % 7))).ToList();
        Assert.Null(ItemTelemetry.PointBiserial(allWrong));

        // every total identical: the test score has no variance either, so r is equally undefined
        var flatTotals = Enumerable.Range(0, 40).Select(i => (Item: i % 2 == 0 ? 1d : 0d, Total: 5d)).ToList();
        Assert.Null(ItemTelemetry.PointBiserial(flatTotals));

        // one observation is not a correlation
        Assert.Null(ItemTelemetry.PointBiserial([(Item: 1d, Total: 9d)]));
        Assert.Null(ItemTelemetry.PointBiserial([]));
    }

    /// <summary>
    /// Condition 1, the boundary pair, and the reason the pair exists: the same statistic (r = 0 by
    /// symmetry) is <em>fine</em> at 48 administrations and <em>failing</em> at 50, because D0.7 sets
    /// 0.00 at N ≥ 30 and 0.10 at N ≥ 50. A gate written once, without the sample-size switch, passes
    /// one half of this test and fails the other whichever threshold it picked.
    /// </summary>
    [Fact]
    public void Discrimination_of_zero_passes_at_48_administrations_and_fails_at_50()
    {
        var at48 = ItemTelemetry.Evaluate(CefrLevel.B1, "A", ZeroCorrelation(24, 24));
        Assert.Equal(48, at48.Administrations);
        Assert.NotNull(at48.Discrimination);
        Assert.Equal(0d, at48.Discrimination!.Value, 12);
        Assert.DoesNotContain(ItemTelemetry.BreachDiscrimination, at48.Breaches);

        var at50 = ItemTelemetry.Evaluate(CefrLevel.B1, "A", ZeroCorrelation(25, 25));
        Assert.Equal(50, at50.Administrations);
        Assert.Equal(0d, at50.Discrimination!.Value, 12);
        Assert.Contains(ItemTelemetry.BreachDiscrimination, at50.Breaches);
    }

    /// <summary>
    /// Condition 1 at the 30 checkpoint: a <em>negative</em> correlation is the only thing that fails
    /// below 50, so an item that mildly separates learners must survive. The trap here is a gate that
    /// reads <c>r &lt;= 0.10</c> unconditionally and therefore quarantines a good item at N = 30.
    /// </summary>
    [Fact]
    public void Discrimination_below_thirty_sample_rules_only_fails_below_zero()
    {
        var good = ItemTelemetry.Evaluate(CefrLevel.B1, "A", PerfectSeparation(15, 15));
        Assert.Equal(30, good.Administrations);
        Assert.Equal(1d, good.Discrimination!.Value, 12);
        Assert.DoesNotContain(ItemTelemetry.BreachDiscrimination, good.Breaches);

        var inverted = ItemTelemetry.Evaluate(CefrLevel.B1, "A", PerfectSeparation(15, 15, invert: true));
        Assert.Equal(-1d, inverted.Discrimination!.Value, 12);
        Assert.Contains(ItemTelemetry.BreachDiscrimination, inverted.Breaches);
    }

    /// <summary>
    /// Nothing is evaluated below 30 administrations. The trap: an evaluation that runs from the first
    /// answer quarantines items on noise, which is the defect the checkpoints exist to prevent.
    /// </summary>
    [Fact]
    public void Below_thirty_administrations_no_condition_is_evaluated()
    {
        var verdict = ItemTelemetry.Evaluate(CefrLevel.B1, "A", PerfectSeparation(14, 14, invert: true));

        Assert.Equal(28, verdict.Administrations);
        Assert.False(verdict.Evaluated);
        Assert.Empty(verdict.Breaches);
    }

    /// <summary>
    /// Condition 2, both boundaries. <c>[0.30, 0.92]</c> is closed, so 0.30 and 0.92 are inside and
    /// 0.29 / 0.93 are outside. The trap: an off-by-one reading of the interval quarantines an item
    /// sitting exactly on its floor, which is an ordinary hard item, not a broken one.
    /// </summary>
    [Theory]
    [InlineData(30, false)]   // 0.30 exactly - inside
    [InlineData(29, true)]    // 0.29 - below the floor
    [InlineData(92, false)]   // 0.92 exactly - inside
    [InlineData(93, true)]    // 0.93 - above the ceiling
    [InlineData(60, false)]   // comfortably inside
    public void Facility_boundaries_are_closed_at_both_ends(int correctOutOf100, bool expectedBreach)
    {
        // 100 target-band administrations, discrimination deliberately healthy so only facility can
        // be the breach: this is the "bad on a different condition, fine on this one" fixture.
        var verdict = ItemTelemetry.Evaluate(CefrLevel.B1, "A", Facility(correctOutOf100, 100));

        Assert.Equal(100, verdict.TargetBandAdministrations);
        Assert.Equal(correctOutOf100 / 100d, verdict.TargetFacility!.Value, 12);
        Assert.Equal(expectedBreach, verdict.Breaches.Contains(ItemTelemetry.BreachFacility));
        Assert.DoesNotContain(ItemTelemetry.BreachDiscrimination, verdict.Breaches);
    }

    /// <summary>
    /// Condition 2 does not run below 50 administrations even when target-band data exists, because
    /// D0.7 names that checkpoint. The trap: applying the facility band at N = 30 turns a small sample
    /// into a quarantine.
    /// </summary>
    [Fact]
    public void Facility_is_not_judged_below_fifty_administrations()
    {
        var verdict = ItemTelemetry.Evaluate(CefrLevel.B1, "A", Facility(5, 40));

        Assert.Equal(40, verdict.Administrations);
        Assert.Equal(0.125d, verdict.TargetFacility!.Value, 12);   // reported, for the console
        Assert.DoesNotContain(ItemTelemetry.BreachFacility, verdict.Breaches);
    }

    /// <summary>
    /// Condition 3 is a conjunction: a distractor above 45% is only a defect when the key has fallen
    /// below 35%. A gate that fires on the distractor alone quarantines an item whose key still
    /// attracts the largest single share of target-band learners.
    /// </summary>
    [Fact]
    public void Distractor_dominance_needs_both_halves_of_the_conjunction()
    {
        // key 0.40 (>= 0.35) and a distractor at 0.50 (> 0.45): NOT a breach
        var keyHolds = ItemTelemetry.Evaluate(CefrLevel.B1, "A", Labels(("A", 40), ("B", 10), ("C", 50), ("D", 0)));
        Assert.Equal(0.40d, keyHolds.TargetFacility!.Value, 12);
        Assert.DoesNotContain(ItemTelemetry.BreachDistractorDominance, keyHolds.Breaches);
        Assert.DoesNotContain(ItemTelemetry.BreachFacility, keyHolds.Breaches);

        // key 0.30 (< 0.35) and a distractor at 0.50 (> 0.45): a breach
        var swallowed = ItemTelemetry.Evaluate(CefrLevel.B1, "A", Labels(("A", 30), ("B", 10), ("C", 50), ("D", 10)));
        Assert.Contains(ItemTelemetry.BreachDistractorDominance, swallowed.Breaches);
        // and facility 0.30 is inside the closed band, so exactly one condition is named
        Assert.DoesNotContain(ItemTelemetry.BreachFacility, swallowed.Breaches);
        Assert.Equal([ItemTelemetry.BreachDistractorDominance], swallowed.Breaches);
    }

    /// <summary>
    /// The two strict inequalities, walked at their boundaries: a distractor at exactly 45% is not
    /// "more than 45%", and a key at exactly 35% is not "below 35%". A gate written with <c>&gt;=</c>
    /// or <c>&lt;=</c> passes the mid-range cases above and fails here.
    /// </summary>
    [Fact]
    public void Distractor_and_key_boundaries_are_strict()
    {
        // exactly 45% for the worst distractor: not more than 45%
        var distractorExactly = ItemTelemetry.Evaluate(CefrLevel.B1, "A", Labels(("A", 20), ("B", 20), ("C", 45), ("D", 15)));
        Assert.DoesNotContain(ItemTelemetry.BreachDistractorDominance, distractorExactly.Breaches);

        // one point over: a breach, with the key at 34%
        var distractorOver = ItemTelemetry.Evaluate(CefrLevel.B1, "A", Labels(("A", 20), ("B", 20), ("C", 46), ("D", 14)));
        Assert.Contains(ItemTelemetry.BreachDistractorDominance, distractorOver.Breaches);

        // key exactly 35% with a swallowing distractor: NOT below 35%, so not a breach
        var keyExactly = ItemTelemetry.Evaluate(CefrLevel.B1, "A", Labels(("A", 35), ("B", 0), ("C", 50), ("D", 15)));
        Assert.DoesNotContain(ItemTelemetry.BreachDistractorDominance, keyExactly.Breaches);
    }

    /// <summary>
    /// Condition 3 counts the <em>key-relative</em> labels only. The trap: a "worst label share"
    /// computed over all four options includes the key itself, so a healthy key at 50% is reported as
    /// the dominant distractor and every well-behaved item is quarantined.
    /// </summary>
    [Fact]
    public void A_dominant_key_is_never_read_as_a_dominant_distractor()
    {
        var verdict = ItemTelemetry.Evaluate(CefrLevel.B1, "A", Labels(("A", 60), ("B", 20), ("C", 10), ("D", 10)));
        Assert.DoesNotContain(ItemTelemetry.BreachDistractorDominance, verdict.Breaches);
    }

    /// <summary>
    /// Condition 4, both sides of the 0.75-band tolerance. The mean AI band is in rubric bands
    /// (0 = Pre-A1 … 6 = C2, D0.6), so it is on the same axis as the item's authored level.
    /// </summary>
    [Theory]
    [InlineData(4.75, false)]  // exactly +0.75 from B2 (rank 4) - inside the tolerance
    [InlineData(4.76, true)]   // one hundredth over
    [InlineData(3.25, false)]  // exactly -0.75
    [InlineData(3.24, true)]   // just under
    public void Productive_drift_boundary_is_strictly_more_than_three_quarters_of_a_band(
        double meanBand, bool expectedBreach)
    {
        var verdict = ItemTelemetry.Evaluate(CefrLevel.B2, null, Productive(meanBand, 30));

        Assert.Equal(30, verdict.Administrations);
        Assert.Equal(meanBand, verdict.MeanResponseBand!.Value, 12);
        Assert.Equal(expectedBreach, verdict.Breaches.Contains(ItemTelemetry.BreachProductiveDrift));
    }

    /// <summary>
    /// Condition 4 needs 30 graded responses; 29 is not evidence. The trap: an item's mean over two
    /// responses is noise, and this is the number that would otherwise drift a whole cell.
    /// </summary>
    [Fact]
    public void Productive_drift_is_not_judged_below_thirty_responses()
    {
        var verdict = ItemTelemetry.Evaluate(CefrLevel.B2, null, Productive(1.0, 29));

        Assert.Equal(29, verdict.Administrations);
        Assert.False(verdict.Evaluated);
        Assert.Empty(verdict.Breaches);
    }

    /// <summary>
    /// The four conditions are independent, so a fixture that trips one must name exactly one. This is
    /// the test that catches a verdict built from "any breach exists" rather than from the breach set.
    /// </summary>
    [Fact]
    public void Each_condition_names_itself_and_only_itself()
    {
        var facilityOnly = ItemTelemetry.Evaluate(CefrLevel.B1, "A", Facility(95, 100));
        Assert.Equal([ItemTelemetry.BreachFacility], facilityOnly.Breaches);

        var driftOnly = ItemTelemetry.Evaluate(CefrLevel.B2, null, Productive(1.0, 40));
        Assert.Equal([ItemTelemetry.BreachProductiveDrift], driftOnly.Breaches);
    }

    /// <summary>
    /// An objective item never carries a productive drift verdict, and a productive task never carries
    /// a facility or distractor verdict — it has no key and no labels. A gate that shared one verdict
    /// shape across both would report a productive task's "0% key share" as distractor dominance.
    /// </summary>
    [Fact]
    public void Objective_and_productive_conditions_do_not_cross_over()
    {
        var productive = ItemTelemetry.Evaluate(CefrLevel.B1, null, Productive(3.0, 60));
        Assert.Empty(productive.Breaches);
        Assert.Null(productive.TargetFacility);

        // 60 objective answers with no key label and no labels at all: a healthy facility, and a
        // distractor verdict needs a key to be relative to, so it must not appear.
        var unlabelled = ItemTelemetry.Evaluate(CefrLevel.B1, null, Facility(36, 60));
        Assert.Equal(0.6d, unlabelled.TargetFacility!.Value, 12);
        Assert.DoesNotContain(ItemTelemetry.BreachDistractorDominance, unlabelled.Breaches);
        Assert.DoesNotContain(ItemTelemetry.BreachProductiveDrift, unlabelled.Breaches);
    }

    // ---- fixtures -------------------------------------------------------------------------------

    /// <summary>
    /// r = 0 exactly, by symmetry: the correct and the wrong group have the same total distribution,
    /// so their means are equal and the coefficient is zero. <paramref name="each"/> of each kind, so
    /// the administration count is <c>2 * each</c>.
    /// </summary>
    private static List<ItemObservation> ZeroCorrelation(int eachCorrect, int eachWrong)
    {
        var observations = new List<ItemObservation>();
        for (var i = 0; i < eachCorrect; i++)
        {
            observations.Add(new ItemObservation("A", true, TestTotal: i % 2 == 0 ? 8 : 4, TargetBand: true, ResponseBand: null));
        }

        for (var i = 0; i < eachWrong; i++)
        {
            observations.Add(new ItemObservation("B", false, TestTotal: i % 2 == 0 ? 8 : 4, TargetBand: true, ResponseBand: null));
        }

        return observations;
    }

    /// <summary>
    /// r = +1 when not inverted, r = -1 when inverted: every learner who scored the item has one total
    /// and every learner who missed it has the other, so the two groups are perfectly ordered.
    /// </summary>
    private static List<ItemObservation> PerfectSeparation(int correct, int wrong, bool invert = false)
    {
        var correctTotal = invert ? 4d : 10d;
        var wrongTotal = invert ? 10d : 4d;

        var observations = new List<ItemObservation>();
        for (var i = 0; i < correct; i++)
        {
            observations.Add(new ItemObservation("A", true, TestTotal: correctTotal, TargetBand: true, ResponseBand: null));
        }

        for (var i = 0; i < wrong; i++)
        {
            observations.Add(new ItemObservation("B", false, TestTotal: wrongTotal, TargetBand: true, ResponseBand: null));
        }

        return observations;
    }

    /// <summary>
    /// A healthy-discrimination item whose target-band facility is <paramref name="correct"/> out of
    /// <paramref name="total"/>. Totals rise with correctness, so no discrimination breach can appear
    /// by accident and the assertion is about facility alone.
    /// </summary>
    private static List<ItemObservation> Facility(int correct, int total)
    {
        var observations = new List<ItemObservation>();
        for (var i = 0; i < total; i++)
        {
            var isCorrect = i < correct;
            observations.Add(new ItemObservation(
                isCorrect ? "A" : "B",
                isCorrect,
                TestTotal: isCorrect ? 9.0 + (i % 3) : 3.0 + (i % 3),
                TargetBand: true,
                ResponseBand: null));
        }

        return observations;
    }

    /// <summary>
    /// One target-band learner per percentage point, with the share each label received. Totals are
    /// ordered so discrimination is healthy: this fixture is about labels, not about r.
    /// </summary>
    private static List<ItemObservation> Labels(params (string Label, int Percent)[] shares)
    {
        var observations = new List<ItemObservation>();
        foreach (var (label, percent) in shares)
        {
            for (var i = 0; i < percent; i++)
            {
                observations.Add(new ItemObservation(
                    label,
                    label == "A",
                    TestTotal: label == "A" ? 9.0 : 5.0,
                    TargetBand: true,
                    ResponseBand: null));
            }
        }

        return observations;
    }

    /// <summary>
    /// <paramref name="count"/> AI-graded responses all at the same band, which is what a mean over a
    /// stored grading sample looks like once the arithmetic is done.
    /// </summary>
    private static List<ItemObservation> Productive(double meanBand, int count) =>
        Enumerable.Range(0, count)
            .Select(_ => new ItemObservation(null, null, TestTotal: 0, TargetBand: true, ResponseBand: meanBand))
            .ToList();
}
