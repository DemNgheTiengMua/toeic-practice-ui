using System;

namespace Contracts;

/// <summary>
/// #153: a learner's goal and the verdict on it. <see cref="Status"/> is null when there is nothing
/// to answer — no deadline, or a certificate whose bands carry no study hours — which is not the same
/// as a refusal.
/// </summary>
public sealed record GoalStatusDto(
    string? CurrentBandCode,
    string TargetBandCode,
    DateOnly? TargetDate,
    decimal? WeeklyStudyHours,
    int? RequiredHours,
    decimal? AvailableHours,
    FeasibilityStatusDto? Status,
    int? SuggestedWeeklyHours,
    DateOnly? EarliestFeasibleDate);
