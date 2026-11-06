namespace Contracts;

/// <summary>#232: a step was asked to start, and this is the surface it opened.</summary>
public sealed record StartStepResultDto(
    int StepId,
    PathStepTypeDto StepType,
    StepRouteDto Route,
    string RedirectUrl,
    int? SessionId,
    int? AttemptId);
