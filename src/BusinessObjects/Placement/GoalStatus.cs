using System;

namespace BusinessObjects;

/// <summary>
/// #153: a learner's goal and what the system makes of it. <see cref="Status"/> is null when there is
/// nothing to answer — no deadline, or bands that carry no study hours — which is not the same thing
/// as <see cref="FeasibilityStatus.Unrealistic"/>. A null verdict is the absence of an answer, so it
/// is modelled as a missing value rather than a fourth enumeration member the database would reject.
/// </summary>
public sealed record GoalStatus(
    string? CurrentBandCode,
    string TargetBandCode,
    DateOnly? TargetDate,
    decimal? WeeklyStudyHours,
    int? RequiredHours,
    decimal? AvailableHours,
    FeasibilityStatus? Status,
    int? SuggestedWeeklyHours,
    DateOnly? EarliestFeasibleDate);
