using BusinessObjects;

namespace Services;

/// <summary>
/// #212 / PLC-33, PLC-36: the one writer of a placement's per-skill results, and the derivation of
/// each skill's status from what the sitting produced.
/// </summary>
public interface IPlacementResultWriter
{
    /// <summary>
    /// Writes one row per skill the placement's paper examined, then decides the placement's own
    /// completion. Idempotent: a second call after grading settles rewrites only what changed.
    /// </summary>
    Task<PlacementWriteOutcome> WriteAsync(int placementId, CancellationToken cancellationToken = default);
}

/// <summary>
/// #212: what a placement's write concluded. <see cref="IsGrading"/> and <see cref="IsDone"/> are
/// mutually exclusive by construction, which is the point: a pending productive task leaves the
/// placement in progress, and only a measurement or a recorded failure finishes it.
/// </summary>
/// <param name="PlacementId">The placement written.</param>
/// <param name="IsDone">At least one skill measured, or a completion failure recorded (PLC-36).</param>
/// <param name="IsGrading">A productive task is still in the AI queue, so the placement stays open.</param>
/// <param name="MeasuredSkillCount">How many skills are a real measurement.</param>
/// <param name="CompletionFailureReason">Why a done placement has no measurement; null otherwise.</param>
public sealed record PlacementWriteOutcome(
    int PlacementId,
    bool IsDone,
    bool IsGrading,
    int MeasuredSkillCount,
    string? CompletionFailureReason);
