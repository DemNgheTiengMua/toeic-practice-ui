namespace BusinessObjects;

/// <summary>#159: "where am I against my goal", as the block shows it. Every number is measured;
/// the block renders this and never derives a second opinion. <see cref="Status"/> mirrors the stored
/// feasibility verdict: null means there is nothing to answer (no deadline, or bands with no hours).</summary>
public sealed record GoalStanding(
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
    int WeekCompletedMinutes,
    int ScoreDisplayFactor = 1);
