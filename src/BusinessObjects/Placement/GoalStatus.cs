using System;

namespace BusinessObjects;

/// <summary>
/// #153: a learner's goal and what the system makes of it. <see cref="Status"/> is
/// <see cref="FeasibilityStatus.Unknown"/> when there is nothing to answer — no deadline, or bands
/// that carry no study hours — which is not the same thing as <see cref="FeasibilityStatus.Unrealistic"/>.
/// </summary>
public sealed record GoalStatus(
    string? CurrentBandCode,
    string TargetBandCode,
    DateOnly? TargetDate,
    decimal? WeeklyStudyHours,
    int? RequiredHours,
    decimal? AvailableHours,
    FeasibilityStatus Status,
    int? SuggestedWeeklyHours,
    DateOnly? EarliestFeasibleDate);
