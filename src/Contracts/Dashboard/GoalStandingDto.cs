using System;

namespace Contracts;

/// <summary>#159: the Goal Standing block's data. <paramref name="Status"/> mirrors the stored
/// feasibility verdict; null means undecidable, which is not the same as a refusal.
/// <para>
/// #241 / D21a: the block is four tabular cells - current level per skill against target, remaining
/// guided hours, pacing comparison, verdict. <paramref name="RequiredTotalHours"/> is the stored
/// ΔH (null when the certificate's bands carry no study hours) and <paramref name="HasNoTarget"/>
/// is the explicit "no goal to judge" state, so the page can read <c>cannot judge</c> rather than
/// inventing a verdict from a missing target (#244's rule, applied to this screen).
/// </para></summary>
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
    int WeekCompletedMinutes,
    int ScoreDisplayFactor = 1,
    int? RequiredTotalHours = null,
    /// <summary>The overall floor and the skill that produced it, from #211's <c>LearnerStanding</c>.</summary>
    string? OverallLevelCode = null,
    string? LimitingSkillCode = null,
    string? LimitingSkillName = null,
    /// <summary>LRN-13: nothing has been measured, so the dashboard is one block and one action.</summary>
    bool HasNoMeasurement = false);
