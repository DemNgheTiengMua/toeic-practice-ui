using System;

namespace BusinessObjects;

/// <summary>
/// #232 / D10: what the learner is shown before a step starts — sequence, skill, topic, level, exact
/// item count and estimated minutes. The four metadata columns the path screen requires (D21a).
/// </summary>
public sealed record PathStepInfo(
    int StepId,
    int WeekNumber,
    int WeekSequence,
    PathStepType StepType,
    string Title,
    LearnerPathStepStatus Status,
    string SkillCode,
    string SkillName,
    Modality Modality,
    string TopicCode,
    string TopicName,
    CefrLevel TargetCefr,
    SizingTier SizingTier,
    int? PlannedItemCount,
    int? EstimatedMinutes,
    decimal? Score,
    decimal? ActualAccuracyPct,
    int TargetAccuracyPct,
    bool CanStart,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int? RefLessonId,
    int? RefExamId,
    string? GuideText = null,
    System.Collections.Generic.IReadOnlyList<string>? RubricCriteria = null,
    bool IsZeroCreditAiGrading = false)
{
    /// <summary>D10: which surface this step opens. Derived from its type, never stored beside it.</summary>
    public StepRoute Route => LearningPathPlanner.ExecutionRouteOf(StepType);
}

/// <summary>
/// #232 / D10: a planning week and its On Track verdict. Computed by the service from the stored rows
/// through <see cref="LearningPathPlanner.IsWeekOnTrack"/> — never by the page.
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
    System.Collections.Generic.List<PathStepInfo> Steps);

/// <summary>
/// #232: where a step opens. The step type decides the route, which is what makes D10's three bindings
/// three bindings rather than one shared label.
/// <para>
/// #235 / PLC-60: <paramref name="SubstitutedFromTopicId"/> is set when the step's own cell was cooling
/// and sibling topics supplied the draw. It is the step's own topic id, so the screen can say "these
/// items come from a related topic while yours rests" rather than silently serving a different topic.
/// </para>
/// </summary>
public sealed record StartStepResult(
    int StepId,
    PathStepType StepType,
    StepRoute Route,
    string RedirectUrl,
    int? SessionId,
    int? AttemptId)
{
    public int? SubstitutedFromTopicId { get; init; }
}
