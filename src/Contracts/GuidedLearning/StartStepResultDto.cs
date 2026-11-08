namespace Contracts;

/// <summary>#232: a step was asked to start, and this is the surface it opened.
/// <para>
/// #235 / PLC-60: <paramref name="SubstitutedFromTopicId"/> is set when the step's own cell was cooling
/// or empty and sibling topics supplied the draw, so the screen can say so instead of silently serving a
/// different topic.
/// </para>
/// </summary>
public sealed record StartStepResultDto(
    int StepId,
    PathStepTypeDto StepType,
    StepRouteDto Route,
    string RedirectUrl,
    int? SessionId,
    int? AttemptId,
    int? SubstitutedFromTopicId = null);
