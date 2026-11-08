namespace BusinessObjects;

/// <summary>
/// #217 / PLC-35: what a grading retry did. <see cref="RequeuedCount"/> is the number of failed tasks
/// put back in the queue; it is zero when there was nothing to retry, which the caller's guard refuses
/// before this is ever returned.
/// </summary>
/// <param name="PlacementId">The placement retried.</param>
/// <param name="AttemptId">Its attempt, whose gradings were re-queued.</param>
/// <param name="RequeuedCount">How many failed gradings went back to pending.</param>
public sealed record PlacementRetryResult(int PlacementId, int AttemptId, int RequeuedCount);
