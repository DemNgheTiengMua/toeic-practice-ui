using System;
using System.Collections.Generic;

namespace Contracts;

/// <summary>#154: one step inside a planning week.</summary>
public sealed record PathWeekStepDto(
    int StepId,
    PathModuleItemTypeDto ItemType,
    string Title,
    LearnerPathStepStatusDto Status,
    int? EstimatedMinutes,
    decimal? Score,
    decimal? ActualAccuracyPct,
    int TargetAccuracyPct,
    bool CanStart,
    DateTimeOffset? CompletedAt,
    int? RefSectionId,
    int? RefExamId,
    int? RefLessonId);

/// <summary>#154: a planning week and its On Track verdict, computed by the service from the stored
/// rows — never by the page.</summary>
public sealed record LearningPathWeekSummaryDto(
    int WeekNumber,
    int PlannedSteps,
    int CompletedSteps,
    int PlannedMinutes,
    int CompletedMinutes,
    bool IsOnTrack,
    int TargetAccuracyPct,
    decimal? ActualAccuracyPct,
    List<PathWeekStepDto> Steps);
