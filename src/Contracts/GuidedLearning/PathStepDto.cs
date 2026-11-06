using System;
using System.Collections.Generic;

namespace Contracts;

/// <summary>
/// #232 / D10: what the learner sees before a step starts — sequence and skill, the canonical topic,
/// the level badge, and the exact item count with estimated minutes (D21a).
/// </summary>
public sealed record PathStepDto(
    int StepId,
    int WeekNumber,
    int WeekSequence,
    PathStepTypeDto StepType,
    StepRouteDto Route,
    string Title,
    LearnerPathStepStatusDto Status,
    string SkillCode,
    string SkillName,
    ModalityDto Modality,
    string TopicCode,
    string TopicName,
    string TargetCefr,
    SizingTierDto SizingTier,
    int? PlannedItemCount,
    int? EstimatedMinutes,
    decimal? Score,
    decimal? ActualAccuracyPct,
    int TargetAccuracyPct,
    bool CanStart,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int? RefLessonId,
    int? RefExamId);

/// <summary>#232: a planning week and its On Track verdict.</summary>
public sealed record LearningPathWeekSummaryDto(
    int WeekNumber,
    int PlannedSteps,
    int CompletedSteps,
    int PlannedMinutes,
    int CompletedMinutes,
    bool IsOnTrack,
    int TargetAccuracyPct,
    decimal? ActualAccuracyPct,
    List<PathStepDto> Steps);
