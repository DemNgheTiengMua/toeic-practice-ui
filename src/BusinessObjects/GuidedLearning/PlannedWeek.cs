using System.Collections.Generic;
using System.Linq;

namespace BusinessObjects;

/// <summary>
/// #234 / PLC-51, PLC-53: one examined skill's share of one week. The minutes are the fact; the step
/// count follows from them, because a count-first rule cannot honour a minute floor and a 12-item tier
/// at the same time.
/// </summary>
/// <param name="SkillId">The certificate's skill.</param>
/// <param name="Weight">`W_s` — the share this skill's distance earned, before rounding.</param>
/// <param name="Minutes">The minutes this skill's sittings add up to.</param>
/// <param name="Steps">How many sittings those minutes are packed into.</param>
/// <param name="StepMinutes">Each sitting's own minutes. Always sums to <paramref name="Minutes"/>.</param>
public sealed record SkillWeekAllocation(
    int SkillId,
    double Weight,
    int Minutes,
    int Steps,
    IReadOnlyList<int> StepMinutes);

/// <summary>
/// #234 / PLC-51, PLC-56: one week of the plan, with the arithmetic that produced it.
/// </summary>
/// <param name="WeekNumber">1–4, the window slot.</param>
/// <param name="BudgetMinutes">`T_weekly` — what the learner can actually spend this week.</param>
/// <param name="Allocations">One entry per examined skill, in the order the week visits them.</param>
public sealed record PlannedWeek(
    int WeekNumber,
    int BudgetMinutes,
    IReadOnlyList<SkillWeekAllocation> Allocations)
{
    public int TotalMinutes => Allocations.Sum(a => a.Minutes);

    public int TotalSteps => Allocations.Sum(a => a.Steps);

    /// <summary>
    /// PLC-56: why this week is not an ordinary one, or null when it is. A week that fits its hours
    /// says nothing — a notice is reserved for the two ways the plan could mislead.
    /// </summary>
    public PathWeekNotice? Notice { get; init; }

    /// <summary>PLC-51: whether the week's sittings are packed to its budget within ±10 minutes. The
    /// tolerance is over the <b>week</b>, not one step: a single 40-minute sitting against a 15-minute
    /// week is exactly the defect, while a week of five sitting sizes that total the budget is not.</summary>
    public bool IsPacked =>
        System.Math.Abs(TotalMinutes - BudgetMinutes) <= LearningPathPlanner.PackingToleranceMinutes;
}

/// <summary>
/// #234 / PLC-56: the three ways a week is not ordinary. A week that fits its hours is none of them.
/// </summary>
public enum PathWeekNotice
{
    /// <summary>The learner has more hours than the goal requires. Up to 30% of the surplus moves to
    /// productive work and cross-topic review, and the week is capped rather than repeating items.</summary>
    SurplusCapped,

    /// <summary>The learner has fewer hours than the goal requires. The week packs strictly inside what
    /// they have and the feasibility verdict carries the three options.</summary>
    DeficitPacked,

    /// <summary>A week too thin to give every examined skill a sitting. The highest-weighted skills are
    /// served and the rest are honestly absent rather than promised a minute that does not exist.</summary>
    TooFewSittings,
}
