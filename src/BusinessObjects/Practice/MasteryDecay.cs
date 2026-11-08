using System;

namespace BusinessObjects;

/// <summary>
/// #237 / PLC-71: the inactivity drift, as one pure function.
/// <para>
/// <b>The rule, quoted:</b> "a mastered cell not practised for 28 days, with no mock section at or
/// above that level in the meantime, decays <c>0.02</c> per week toward the prior. Below 0.80 it
/// reverts to developing and resumes standard sizing until a maintenance check re-verifies it."
/// </para>
/// <para>
/// <b>Taken literally: 0.02 per week, floored at the prior.</b> "Toward the prior" is the direction,
/// not a proportional step — the budget line states the unit (<c>0.02 per week</c>) and a subtraction
/// that stops at <c>0.5</c> is the reading that needs no second constant. A cell at <c>0.85</c> is
/// <c>0.83</c> after one week and crosses <c>0.80</c> after three; a cell sitting exactly on the
/// <c>0.80</c> boundary reverts after one.
/// </para>
/// </summary>
public static class MasteryDecay
{
    /// <summary>
    /// Whole weeks the cell is overdue past the 28-day onset. 28 days exactly is zero: the rule says
    /// "not practised for 28 days", so the drift starts on day 29.
    /// </summary>
    public static int WeeksOverdue(DateTimeOffset lastPractisedAt, DateTimeOffset at)
    {
        var idleDays = (at - lastPractisedAt).TotalDays;
        if (idleDays <= MasterySchedule.InactivityOnsetDays)
        {
            return 0;
        }

        return (int)Math.Floor((idleDays - MasterySchedule.InactivityOnsetDays) / 7d);
    }

    /// <summary>
    /// PLC-71 applied to a probability: <c>0.02</c> per week, never below the prior.
    /// <para>
    /// A value at or below the prior is returned unchanged rather than lifted to it: <c>0.5</c> is the
    /// destination of the drift, not a floor a weak cell is raised to. Lifting would turn a
    /// <c>learning</c> cell at <c>0.40</c> into a <c>reviewing</c> one over the calendar alone.
    /// </para>
    /// </summary>
    public static double Apply(double probability, int weeks)
    {
        if (weeks <= 0 || probability <= MasterySchedule.PriorProbability)
        {
            return probability;
        }

        // Rounded to the precision the estimate is stated at. Without it 0.82 - 0.02 is
        // 0.7999999999999999 in double, so a cell sitting exactly on the 0.80 cut would be reported
        // reverted by one week of decay that mathematically leaves it exactly on the cut.
        return Math.Max(
            MasterySchedule.PriorProbability,
            Math.Round(probability - (MasterySchedule.WeeklyDecay * weeks), 6));
    }

    /// <summary>
    /// The state a <b>mastered</b> cell's decayed estimate lands in: <c>mastered</c> while it holds the
    /// upper cut, <c>reviewing</c> once it falls below.
    /// <para>
    /// It is deliberately only defined for a mastered cell — the two outcomes are the only two PLC-71
    /// names ("reverts to developing"). A cell that was never mastered is not decayed at all, so asking
    /// this function about one is a category error and <see cref="ApplyIfMastered"/> is the guard that
    /// prevents it: there is no third answer for a `learning` cell to be given here.
    /// </para>
    /// </summary>
    public static MasteryState StateAfterDecay(double probability, int weeks) =>
        Apply(probability, weeks) < MasterySchedule.MasteredThreshold
            ? MasteryState.Reviewing
            : MasteryState.Mastered;

    /// <summary>
    /// The decay as a state transition. A cell that was never <c>mastered</c> is returned untouched:
    /// there is no held position to erode, and "recompute from age" would silently re-grade every weak
    /// cell the learner has simply not returned to.
    /// </summary>
    public static double ApplyIfMastered(double probability, MasteryState state, int weeks) =>
        state == MasteryState.Mastered ? Apply(probability, weeks) : probability;
}
