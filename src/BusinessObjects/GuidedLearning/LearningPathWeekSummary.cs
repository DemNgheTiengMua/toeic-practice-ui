using System;
using System.Collections.Generic;

namespace BusinessObjects;

/// <summary>#154: one step as the path screen shows it — a week's row, with what was planned and what
/// was measured. Distinct from <see cref="ModuleStepInfo"/>, which is the module drill-down's view.</summary>
public sealed record PathWeekStepInfo(
    int StepId,
    PathModuleItemType ItemType,
    string Title,
    LearnerPathStepStatus Status,
    int? EstimatedMinutes,
    decimal? Score,
    decimal? ActualAccuracyPct,
    int TargetAccuracyPct,
    bool CanStart,
    DateTimeOffset? CompletedAt,
    int? RefSectionId,
    int? RefExamId,
    int? RefLessonId);

/// <summary>
/// #154: a planning week. "On track" is the ticket's rule: at least 80% of the week's steps done
/// **and** measured accuracy at or above the week's target. Computed by
/// <see cref="LearningPathPlanner.IsWeekOnTrack"/>, never by the view.
/// </summary>
public sealed record LearningPathWeekSummary(
    int WeekNumber,
    int PlannedSteps,
    int CompletedSteps,
    int PlannedMinutes,
    int CompletedMinutes,
    bool IsOnTrack,
    int TargetAccuracyPct,
    decimal? ActualAccuracyPct,
    List<PathWeekStepInfo> Steps);
