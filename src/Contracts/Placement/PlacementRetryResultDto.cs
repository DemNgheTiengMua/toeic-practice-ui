namespace Contracts;

/// <summary>
/// #217 / PLC-35: what a placement grading retry did. The learner is offered it because a failed
/// grading is the system's fault; the retry re-queues the same tasks and writes no ledger row.
/// </summary>
/// <param name="PlacementId">The placement retried.</param>
/// <param name="AttemptId">Its attempt, whose gradings were re-queued.</param>
/// <param name="RequeuedCount">How many failed gradings went back to pending.</param>
public sealed record PlacementRetryResultDto(int PlacementId, int AttemptId, int RequeuedCount);
