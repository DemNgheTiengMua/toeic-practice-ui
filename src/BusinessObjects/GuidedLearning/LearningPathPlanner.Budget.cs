using System;
using System.Collections.Generic;
using System.Linq;

namespace BusinessObjects;

/// <summary>
/// #234 / PLC-51, PLC-53: the weekly volume and its per-skill allocation, as pure functions.
/// <para>
/// <b>The minutes are the fact and the step count follows from them.</b> The rule this replaces counted
/// steps and divided the budget by that count, which cannot honour a minute floor (a 12-item tier is not
/// a 6-minute sitting) and cannot tell 600 minutes in five sittings from 1200 in the same five. Both
/// defects were measured on <c>9e40a79</c>: 10 h/week and 20 h/week planned the same five steps, and a
/// learner who declared one hour was promised three.
/// </para>
/// <para>
/// Every number here is either quoted from the specification or derived from a quoted number:
/// </para>
/// <list type="bullet">
/// <item><c>H_required = Î£ H_s(current â†’ target) / max(1, days remaining / 7)</c> â€” SRS.md:811.</item>
/// <item><c>T_weekly = 60 Ã— min(H_weekly_target, max(1.5, H_required))</c> â€” SRS.md:812. The floor is on
/// the <b>required velocity</b>, in hours, so it can never raise a plan above the pace the learner
/// declared: the <c>min</c> is what keeps a one-hour learner at one hour.</item>
/// <item><c>W_s = d_s^1.5 / Î£ d_j^1.5</c>, <c>d_s = max(0.15, Î¸_target âˆ’ Î¸_current)</c> â€” SRS.md:819.</item>
/// <item>"Every examined skill MUST receive at least 1 step per week" â€” SRS.md:821.</item>
/// <item>"Steps are packed to that budget Â±10 minutes" â€” SRS.md:813.</item>
/// <item>"up to 30% of surplus minutes move to productive work and cross-topic review" â€” SRS.md:833.</item>
/// </list>
/// </summary>
public static partial class LearningPathPlanner
{
    /// <summary>PLC-53: `Î³`. A provisional baseline (OQ-04), not a tuned value.</summary>
    public const decimal SkewExponent = 1.5m;

    /// <summary>PLC-53: `d_s = max(0.15, Î¸_target âˆ’ Î¸_current)`. Also the distance assumed for a skill
    /// the caller has no target coordinate for, which is why "no target at all" falls out of the same
    /// formula as a flat week: every skill at the floor has an equal weight.</summary>
    public const decimal DistanceFloor = 0.15m;

    /// <summary>PLC-51: the floor on `H_required`, in hours per week. It bounds the <b>required</b>
    /// velocity; it never raises the plan above `H_weekly_target`.</summary>
    public const decimal MinimumRequiredWeeklyHours = 1.5m;

    /// <summary>PLC-51: "steps are packed to that budget Â±10 minutes". Measured over the whole week â€”
    /// the sum of its steps' minutes against `T_weekly` â€” never per step.</summary>
    public const int PackingToleranceMinutes = 10;

    /// <summary>PLC-56: "up to 30% of surplus minutes".</summary>
    public const decimal SurplusProductiveShare = 0.30m;

    /// <summary>The per-skill pacing a learner's goal produces. Kept as one value so a caller cannot
    /// pass a target pace without the velocity it is compared against, which is how the old rule
    /// silently ignored the distance to target.</summary>
    /// <param name="WeeklyTargetHours">`H_weekly_target` â€” what the learner said they can spend.</param>
    /// <param name="RemainingStudyHours">`Î£ H_s(current â†’ target)` from
    /// <c>ScoreBands.CumulativeStudyHours</c>, or null when the certificate's bands carry no study-hour
    /// evidence (`CANNOT_JUDGE`, D0.10).</param>
    /// <param name="DaysRemaining">`D_remaining` â€” days until the target date.</param>
    public sealed record WeeklyPacing(
        decimal WeeklyTargetHours,
        decimal? RemainingStudyHours,
        int DaysRemaining);

    /// <summary>
    /// #234: the pace a goal with no deadline and no band hours can state. Both halves are unknowable,
    /// so the required velocity is null and `T_weekly` is simply the learner's own pace â€” the one branch
    /// that invents nothing, because the learner already said the number.
    /// </summary>
    public static WeeklyPacing PacingFromGoal(decimal weeklyTargetHours) =>
        new(weeklyTargetHours, RemainingStudyHours: null, DaysRemaining: 0);

    /// <summary>
    /// PLC-51: `H_required`. Null when the bands carry no study-hour evidence â€” there is no velocity to
    /// require, and inventing one would turn `CANNOT_JUDGE` into a confident plan.
    /// </summary>
    public static decimal? RequiredWeeklyHours(decimal? remainingStudyHours, int daysRemaining)
    {
        if (remainingStudyHours is null)
        {
            return null;
        }

        var weeks = Math.Max(1m, daysRemaining / 7m);
        return Math.Max(0m, remainingStudyHours.Value) / weeks;
    }

    /// <summary>
    /// PLC-51: `T_weekly = 60 Ã— min(H_weekly_target, max(1.5, H_required))`.
    /// <para>
    /// The `min` is load-bearing and is the half that fixes the measured defect: a learner who declares
    /// one hour gets sixty minutes whatever the deficit says, because promising three hours to someone
    /// with one is the failure the old floor was mistaken for. The `max(1.5, â€¦)` floors the
    /// <b>velocity</b>, so a learner already at target still gets a real week rather than a token one.
    /// </para>
    /// </summary>
    public static int WeeklyBudgetMinutes(WeeklyPacing pacing)
    {
        var required = RequiredWeeklyHours(pacing.RemainingStudyHours, pacing.DaysRemaining);
        var hours = required is null
            ? pacing.WeeklyTargetHours
            : Math.Min(pacing.WeeklyTargetHours, Math.Max(MinimumRequiredWeeklyHours, required.Value));

        return (int)Math.Round(Math.Max(0m, hours) * 60m);
    }

    /// <summary>
    /// PLC-53: the weights, `W_s = d_s^1.5 / Î£ d_j^1.5`. A skill with no target coordinate contributes
    /// the floor, so an unmeasured skill earns its guaranteed step but never the largest share on no
    /// evidence. When <b>no</b> block has a distance the week is flat â€” equal shares â€” because there is
    /// no distance to allocate from and dividing by an all-floor denominator would merely restate that.
    /// </summary>
    public static IReadOnlyList<double> SkillWeights(IReadOnlyList<SkillPlanBlock> blocks)
    {
        if (blocks.Count == 0)
        {
            return [];
        }

        var powered = blocks
            .Select(b => Math.Pow((double)Math.Max(DistanceFloor, b.DistanceToTarget ?? DistanceFloor), (double)SkewExponent))
            .ToArray();
        var total = powered.Sum();

        return total <= 0
            ? Enumerable.Repeat(1d / blocks.Count, blocks.Count).ToList()
            : powered.Select(p => p / total).ToList();
    }

    /// <summary>#234: the one sitting a skill is guaranteed before any share is distributed. The tier's
    /// <b>longest</b> sitting, so a week spends as few sittings as its minutes allow rather than
    /// fragmenting into many short ones.</summary>
    public static int SittingMinutesFor(SizingTier tier) =>
        Math.Clamp(MinuteRangeFor(tier).MaxMinutes, MinimumStepMinutes, MaximumStepMinutes);

    /// <summary>How many sittings a skill's minutes are packed into. A skill with minutes always gets at
    /// least one: zero here would silently drop PLC-53's guarantee.</summary>
    public static int StepsForMinutes(int minutes) =>
        minutes <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(minutes / (double)MaximumStepMinutes));

    /// <summary>Each sitting's minutes. Splits as evenly as whole minutes allow, so no step is more than
    /// one minute longer than another and none exceeds <see cref="MaximumStepMinutes"/>.</summary>
    public static IReadOnlyList<int> PackMinutes(int minutes, int steps)
    {
        if (steps <= 0)
        {
            return [];
        }

        var baseMinutes = minutes / steps;
        var remainder = minutes % steps;
        return Enumerable.Range(0, steps).Select(i => baseMinutes + (i < remainder ? 1 : 0)).ToList();
    }

    /// <summary>
    /// #234: one week, allocated. Every examined skill is guaranteed one sitting first; the rest of the
    /// budget is then distributed by `W_s`.
    /// <para>
    /// <b>That is how the floor and the skew coexist without breaking the total.</b> The floor is a
    /// reservation taken <i>before</i> the weights are applied, not a clamp applied after them: the
    /// minutes a floored skill was short are taken from the skills that were above their own sitting, in
    /// proportion to how far above, so the weights still sum to what remains and the week still sums to
    /// <paramref name="budgetMinutes"/>. A clamp applied afterwards is the version this rule exists to
    /// avoid â€” it leaves the weights summing to more than the week.
    /// </para>
    /// </summary>
    public static PlannedWeek AllocateWeek(int weekNumber, int budgetMinutes, IReadOnlyList<SkillPlanBlock> blocks)
    {
        if (blocks.Count == 0 || budgetMinutes <= 0)
        {
            return new PlannedWeek(weekNumber, budgetMinutes, []);
        }

        var weights = SkillWeights(blocks);
        var sitting = blocks.Select(b => SittingMinutesFor(SizingTierFor(b.Mastery))).ToArray();
        var minutes = AllocateMinutes(budgetMinutes, weights, sitting, out var tooFewSittings);

        var allocations = new List<SkillWeekAllocation>(blocks.Count);
        for (var i = 0; i < blocks.Count; i++)
        {
            var steps = StepsForMinutes(minutes[i]);
            allocations.Add(new SkillWeekAllocation(
                blocks[i].SkillId, weights[i], minutes[i], steps, PackMinutes(minutes[i], steps)));
        }

        return new PlannedWeek(weekNumber, budgetMinutes, allocations)
        {
            Notice = tooFewSittings ? PathWeekNotice.TooFewSittings : null,
        };
    }

    private static int[] AllocateMinutes(
        int budgetMinutes, IReadOnlyList<double> weights, int[] sitting, out bool tooFewSittings)
    {
        tooFewSittings = false;
        var count = sitting.Length;

        if (sitting.Sum() > budgetMinutes)
        {
            // The week cannot hold one full sitting per skill. An equal slice per skill is still a step
            // each when it reaches the shrunken minimum; below that, the highest-weighted skills are
            // served and the rest are honestly absent rather than promised a minute that is not there.
            var per = budgetMinutes / count;
            if (per >= MinimumStepMinutes)
            {
                var even = Enumerable.Repeat(per, count).ToArray();
                foreach (var i in ByWeightDescending(weights).Take(budgetMinutes - per * count))
                {
                    even[i]++;
                }

                return even;
            }

            tooFewSittings = true;
            var served = new int[count];
            var slots = budgetMinutes / MinimumStepMinutes;
            foreach (var i in ByWeightDescending(weights).Take(slots))
            {
                served[i] = MinimumStepMinutes;
            }

            return served;
        }

        var raw = Enumerable.Range(0, count).Select(i => budgetMinutes * weights[i]).ToArray();
        var shortfall = 0d;
        var excess = new double[count];
        var excessTotal = 0d;

        for (var i = 0; i < count; i++)
        {
            if (raw[i] < sitting[i])
            {
                shortfall += sitting[i] - raw[i];
            }
            else
            {
                excess[i] = raw[i] - sitting[i];
                excessTotal += excess[i];
            }
        }

        var exact = new int[count];
        for (var i = 0; i < count; i++)
        {
            exact[i] = raw[i] < sitting[i]
                ? sitting[i]
                : (int)Math.Round(raw[i] - (excessTotal > 0 ? shortfall * excess[i] / excessTotal : 0));
        }

        // Whole minutes, exact total. The residue is the limiting skill's, because it is the one the
        // week is for, and it is at most a few minutes â€” well inside PLC-51's Â±10.
        exact[ByWeightDescending(weights).First()] += budgetMinutes - exact.Sum();
        return exact;
    }

    private static IEnumerable<int> ByWeightDescending(IReadOnlyList<double> weights) =>
        Enumerable.Range(0, weights.Count).OrderByDescending(i => weights[i]).ThenBy(i => i);
}







