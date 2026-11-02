using System;

namespace Contracts;

/// <summary>#159: the Goal Standing block's data. <paramref name="Status"/> mirrors the stored
/// feasibility verdict; null means undecidable, which is not the same as a refusal.</summary>
public sealed record GoalStandingDto(
    string? CurrentBandCode,
    string? CurrentBandName,
    int? CurrentScore,
    string? CurrentSource,
    string? TargetBandCode,
    string? TargetBandName,
    int? TargetMinScore,
    DateOnly? TargetDate,
    decimal? WeeklyStudyHours,
    string? Status,
    int CompletedPathSteps,
    int TotalPathSteps,
    int? CurrentWeekNumber,
    int WeekPlannedMinutes,
    int WeekCompletedMinutes);
