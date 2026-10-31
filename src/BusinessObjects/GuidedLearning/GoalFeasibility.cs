using System;

namespace BusinessObjects;

/// <summary>
/// #153: the answer to "can I reach this band by this date?", with the numbers it was derived from.
/// <see cref="RequiredHours"/> and <see cref="AvailableHours"/> are the two numbers the learner is
/// shown, so a refusal never has to be taken on trust.
/// </summary>
/// <param name="Status">Feasible, Stretch or Unrealistic. Never Unknown: an undecidable goal has no
/// answer to give, so <see cref="GoalFeasibilityCalculator.Evaluate"/> returns null instead.</param>
/// <param name="RequiredHours">ΔH — the study hours between the two bands, floored at the minimum.</param>
/// <param name="WeeksAvailable">W — whole weeks between today and the target date, floored at one.</param>
/// <param name="AvailableHours">H_avail — weeks times the learner's weekly pace.</param>
/// <param name="Ratio">R — available hours over required hours.</param>
/// <param name="SuggestedWeeklyHours">For Stretch: the pace that would make the goal feasible.</param>
/// <param name="EarliestFeasibleDate">For Unrealistic: the date this pace would actually reach.</param>
public sealed record GoalFeasibility(
    FeasibilityStatus Status,
    int RequiredHours,
    int WeeksAvailable,
    decimal AvailableHours,
    decimal Ratio,
    int SuggestedWeeklyHours,
    DateOnly EarliestFeasibleDate);

/// <summary>
/// #153: the feasibility formula. ΔH = max(30, H(target) − H(current)),
/// W = max(1, ceil((TargetDate − today) / 7)), H_avail = W × WeeklyStudyHours, R = H_avail / ΔH.
/// Pure — no database, no clock, no AI. The narrative an insight may add describes these numbers;
/// it never re-derives them (handoff contract §6).
/// </summary>
public static class GoalFeasibilityCalculator
{
    /// <summary>A goal is never cheaper than this, so "same band tomorrow" is not a free pass.</summary>
    public const int MinimumRequiredHours = 30;

    /// <summary>The spec's ceiling on any study plan. Above it, a goal is not livable.</summary>
    public const decimal MaxWeeklyHours = 25m;

    public const decimal FeasibleRatio = 0.85m;

    public const decimal StretchFloor = 0.50m;

    /// <summary>Returns null when the certificate's bands carry no cumulative hours — the goal cannot
    /// be judged, and inventing an answer would turn an unknown into a confident refusal.</summary>
    public static GoalFeasibility? Evaluate(
        int? currentBandHours,
        int? targetBandHours,
        DateOnly targetDate,
        decimal weeklyHours,
        DateOnly today)
    {
        if (currentBandHours is null || targetBandHours is null)
        {
            return null;
        }

        var required = Math.Max(MinimumRequiredHours, targetBandHours.Value - currentBandHours.Value);
        var weeks = Math.Max(1, (int)Math.Ceiling((targetDate.DayNumber - today.DayNumber) / 7.0));
        var available = weeks * weeklyHours;
        var ratio = available / required;
        var neededWeekly = Math.Ceiling((decimal)required / weeks);

        // Two ways to be impossible: the learner cannot fit the hours in the runway (R < 0.50), or
        // the plan needs more than a person can sustain (> 25 h/week) — whether the learner declared
        // that pace themselves or the runway forces it.
        var status = ratio < StretchFloor || weeklyHours > MaxWeeklyHours || neededWeekly > MaxWeeklyHours
            ? FeasibilityStatus.Unrealistic
            : ratio < FeasibleRatio
                ? FeasibilityStatus.Stretch
                : FeasibilityStatus.Feasible;

        return new GoalFeasibility(
            status,
            required,
            weeks,
            available,
            ratio,
            SuggestedWeeklyHours: (int)neededWeekly,
            EarliestFeasibleDate: today.AddDays((int)Math.Ceiling(required / weeklyHours) * 7));
    }
}
