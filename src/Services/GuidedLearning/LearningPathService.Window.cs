using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #233 / D10: the learner-facing door to the window. The slide's rules live in
/// <see cref="PathWindowSynthesizer"/>; this only finds the active path and hands over the pacing and
/// blocks it needs, so the far end is planned exactly as the original four weeks were.
/// </summary>
public sealed partial class LearningPathService
{
    public async Task<int> AdvanceWindowAsync(
        int userId, DateOnly today, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);

        var path = await db.LearnerPaths
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CertId == activeCertId && p.Status == LearnerPathStatus.Active.ToCode())
            .Select(p => new { p.LearnerPathId })
            .FirstOrDefaultAsync(cancellationToken);

        if (path is null)
        {
            return 0;
        }

        var synthesizer = new PathWindowSynthesizer(db);

        return await synthesizer.AdvanceAsync(
            path.LearnerPathId,
            today,
            ct => PacingForSlideAsync(userId, activeCertId, ct),
            ct => BlocksForSlideAsync(userId, activeCertId, ct),
            cancellationToken);
    }

    /// <summary>
    /// The pacing the far end is sized by, resolved through the same
    /// <c>ResolvePacingAndBlocksAsync</c> generation uses. A certificate whose bands carry no study-hour
    /// evidence falls back to the learner's own declared pace — the one number nobody invented.
    /// </summary>
    private async Task<LearningPathPlanner.WeeklyPacing> PacingForSlideAsync(
        int userId, int certId, CancellationToken cancellationToken)
    {
        var (pacing, _) = await ResolveForSlideAsync(userId, certId, cancellationToken);
        return pacing;
    }

    private async Task<System.Collections.Generic.IReadOnlyList<SkillPlanBlock>> BlocksForSlideAsync(
        int userId, int certId, CancellationToken cancellationToken)
    {
        var (_, blocks) = await ResolveForSlideAsync(userId, certId, cancellationToken);
        return blocks;
    }

    /// <summary>
    /// Resolves both halves once. The slide asks for them through two callbacks because the synthesizer
    /// reads them at different moments; the work behind them is one pair of queries per slide, not two.
    /// </summary>
    private async Task<(LearningPathPlanner.WeeklyPacing Pacing, System.Collections.Generic.IReadOnlyList<SkillPlanBlock> Blocks)>
        ResolveForSlideAsync(int userId, int certId, CancellationToken cancellationToken)
    {
        var savedTarget = await db.TargetBands
            .AsNoTracking()
            .FirstOrDefaultAsync(tb => tb.UserId == userId && tb.CertId == certId, cancellationToken);

        var scoreBands = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == certId)
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.MinTotal)
            .ToListAsync(cancellationToken);

        var path = await db.LearnerPaths
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CertId == certId && p.Status == LearnerPathStatus.Active.ToCode())
            .Select(p => new { p.CurrentBandCode })
            .FirstOrDefaultAsync(cancellationToken);

        // The band the far end aims at is the learner's saved target. A path with no goal aims nowhere
        // in particular, so it reuses the band it was built at: an invented band would be a second
        // answer to "where am I going".
        var toBand = savedTarget?.TargetBandCode ?? path?.CurrentBandCode ?? "";
        var fromBand = path?.CurrentBandCode ?? toBand;

        return await ResolvePacingAndBlocksAsync(
            userId, certId, savedTarget, fromBand, toBand, scoreBands, skillLevels: null, cancellationToken);
    }
}
