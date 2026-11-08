using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #216 / PLC-13, PLC-14: the placement-specific half of resume and expiry. It is an adapter over
/// <see cref="IAttemptEngine"/> like the rest of this service - the engine owns the sitting, this
/// owns what a placement means.
/// </summary>
public sealed partial class PlacementService
{
    /// <summary>
    /// PLC-14: what a returning learner sees before choosing. A read: the identity is checked here,
    /// the facts come from the engine, and nothing is stamped.
    /// </summary>
    public async Task<PlacementResumeView> GetResumeAsync(
        int userId,
        int placementId,
        CancellationToken cancellationToken = default)
    {
        var attemptId = await db.PlacementTests
            .Where(p => p.PlacementId == placementId && p.UserId == userId)
            .Select(p => p.AttemptId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PlacementException(PlacementException.PlacementNotFound, $"Placement test {placementId} not found.");

        try
        {
            return await _attemptEngine.GetPlacementResumeAsync(attemptId, userId, cancellationToken);
        }
        catch (AttemptEngineException ex)
        {
            throw MapPlacementException(ex);
        }
    }

    /// <summary>
    /// PLC-14's "explicit abandon". The lifecycle act, not a new state machine: the placement and its
    /// linked attempt both move to their existing <c>abandoned</c> codes, exactly as starting a second
    /// sitting already does for the first. No result is derived, so no writer is involved and
    /// §1.8/ADR-0012 are untouched.
    /// </summary>
    public async Task AbandonAsync(int userId, int placementId, CancellationToken cancellationToken = default)
    {
        var placement = await db.PlacementTests
            .Include(p => p.ExamAttempt)
            .SingleOrDefaultAsync(p => p.PlacementId == placementId && p.UserId == userId, cancellationToken)
            ?? throw new PlacementException(PlacementException.PlacementNotFound, $"Placement test {placementId} not found.");

        if (placement.Status == PlacementStatus.Done.ToCode())
        {
            throw new PlacementException(PlacementException.PlacementNotActive, "This placement test is already finished.");
        }

        if (placement.Status == PlacementStatus.Abandoned.ToCode())
        {
            return;
        }

        placement.Status = PlacementStatus.Abandoned.ToCode();
        if (placement.ExamAttempt is { } attempt && attempt.Status == AttemptStatus.InProgress.ToCode())
        {
            attempt.Status = AttemptStatus.Abandoned.ToCode();
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// PLC-15: one interrupted recording. The rule and its durable counter live on the engine; this
    /// only resolves the placement and refuses one that is not in progress.
    /// </summary>
    public async Task<PlacementRecordingDecision> RecordRecordingInterruptionAsync(
        int userId,
        int placementId,
        int questionId,
        string sessionToken,
        int capturedMs,
        CancellationToken cancellationToken = default)
    {
        var placement = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.PlacementId == placementId && p.UserId == userId)
            .Select(p => new { p.AttemptId, p.Status })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PlacementException(PlacementException.PlacementNotFound, $"Placement test {placementId} not found.");

        if (placement.Status != PlacementStatus.InProgress.ToCode())
        {
            throw new PlacementException(PlacementException.PlacementNotActive, "Placement test is not in progress.");
        }

        try
        {
            return await _attemptEngine.RecordPlacementRecordingInterruptionAsync(
                placement.AttemptId ?? 0, questionId, userId, sessionToken, capturedMs, cancellationToken);
        }
        catch (AttemptEngineException ex)
        {
            throw MapPlacementException(ex);
        }
    }
}
