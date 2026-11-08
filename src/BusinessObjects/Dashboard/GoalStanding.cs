namespace BusinessObjects;

/// <summary>#159: "where am I against my goal", as the block shows it. Every number is measured;
/// the block renders this and never derives a second opinion. <see cref="Status"/> mirrors the stored
/// feasibility verdict: null means there is nothing to answer (no deadline, or bands with no hours).
/// <para>
/// #241: <see cref="RequiredTotalHours"/> is D21a's "remaining guided hours" - the stored
/// <c>TargetBands.RequiredTotalHours</c>, not a fresh ΔH. Reading the stored number is what keeps
/// this block from disagreeing with the goal screen that wrote it (#234's no-proration rule: a
/// certificate whose bands carry no study hours has no ΔH at all, so the field is null rather than
/// an estimate).
/// </para></summary>
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
    int ScoreDisplayFactor = 1,
    /// <summary>ΔH as stored on the goal row. Null when the certificate's bands carry no hours, or
    /// when no goal is set - both mean the verdict cannot be judged, never "zero hours".</summary>
    int? RequiredTotalHours = null)
{
    public bool HasTarget => !string.IsNullOrEmpty(TargetBandCode);

    public bool HasDate => TargetDate is not null;
}
