using System;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AttemptEngine
{
    /// <summary>
    /// #276: record the band pair stage one routed one skill to - <c>AttemptStageBundles</c> row
    /// (<c>StageNumber = 2</c>).
    /// <para>
    /// <b>Why the writer is here and the decision is not.</b> ADR-0012 gives this table exactly one
    /// writer, <c>AttemptEngine</c>, and the trigger refuses any other declaration. The routing decision
    /// is arithmetic over a stage-one score, so it belongs to the caller that holds the draw services
    /// (<see cref="PlacementService"/>); what this method owns is the write, and it enforces the three
    /// preconditions that make the row true: the sitting is still open, its exam is a placement, and
    /// stage one for that skill was actually served. A stage-two row with no stage-one row beside it
    /// would claim a route nothing produced.
    /// </para>
    /// <para>
    /// <b>Idempotent per skill.</b> The primary key is (attempt, skill, stage), so a repeated boundary
    /// request has nothing to write; it returns <c>false</c> rather than raising a duplicate-key failure
    /// into a learner's sitting.
    /// </para>
    /// </summary>
    /// <returns>True when this call wrote the row; false when there was nothing to write.</returns>
    public async Task<bool> RecordStageTwoRouteAsync(
        int attemptId,
        int userId,
        int skillId,
        CefrLevel bandLower,
        CefrLevel bandUpper,
        CancellationToken cancellationToken = default)
    {
        if (bandLower >= bandUpper)
        {
            // CK_ASB_BandOrder: the pair is an interval, never a point and never inverted.
            return false;
        }

        var attempt = await LoadSittingAttemptAsync(
            db.ExamAttempts.Include(a => a.Exam), attemptId, userId, cancellationToken);

        if (!string.Equals(attempt.Exam.Purpose, ExamPurpose.Placement.ToCode(), StringComparison.Ordinal))
        {
            return false;
        }

        var stageOneServed = await db.AttemptStageBundles
            .AsNoTracking()
            .AnyAsync(b => b.AttemptId == attemptId && b.SkillId == skillId && b.StageNumber == 1, cancellationToken);

        if (!stageOneServed)
        {
            return false;
        }

        var alreadyRouted = await db.AttemptStageBundles
            .AnyAsync(b => b.AttemptId == attemptId && b.SkillId == skillId && b.StageNumber == 2, cancellationToken);

        if (alreadyRouted)
        {
            return false;
        }

        db.AttemptStageBundles.Add(new AttemptStageBundle
        {
            AttemptId = attempt.AttemptId,
            CertId = attempt.CertId,
            SkillId = skillId,
            StageNumber = 2,
            BandLower = bandLower,
            BandUpper = bandUpper,
            WrittenByService = DerivedFactWriters.AttemptPaper,
            ProjectionVersion = 1,
        });

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
