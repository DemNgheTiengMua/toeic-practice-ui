using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #213 / SCR-PL-01, story 51-55: the preflight gate — what the placement is about to measure, what may
/// be carried over from an earlier sitting, and whether a retake is allowed yet.
/// <para>
/// <b>The cooldown lives here, on the placement start, and nowhere else.</b> Story 87 requires a path
/// step never to be blocked by a cooldown; <c>LearningPathService</c> reads the latest placement row
/// directly and never calls this type, so this rule cannot reach a step. Putting it in a shared helper
/// the path service could call is exactly how that invariant would break without anyone noticing.
/// </para>
/// <para>
/// <b>Placement is free, and this file is part of that promise.</b> Nothing here reads a credit balance
/// and nothing here writes a ledger row. The preflight is the first screen a learner sees on this path,
/// so a balance lookup added here would put a price on a free flow — the criterion says "no balance is
/// read", not merely "no row is written".
/// </para>
/// </summary>
public sealed partial class PlacementService
{
    public async Task<PlacementPreflightView> GetPreflightAsync(int userId, CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var now = _clock.GetUtcNow();

        var certCode = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == certId)
            .Select(c => c.Code)
            .SingleAsync(cancellationToken);

        var skills = await db.Skills
            .AsNoTracking()
            .Where(s => s.CertId == certId)
            .OrderBy(s => s.DisplayOrder).ThenBy(s => s.SkillId)
            .Select(s => new { s.SkillId, s.Code, s.Name, s.Modality })
            .ToListAsync(cancellationToken);

        // The reuse window is the STORED one, not a second window computed here: LearnerSkillStates'
        // ExpiresAt is pinned to MeasuredAt + 90 days by CK_LSS_Expiry, and MeasuredAt is the original
        // measurement. Reading them is what makes a carried skill keep its own date.
        var states = await db.LearnerSkillStates
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.CertId == certId)
            .ToDictionaryAsync(s => s.SkillId, cancellationToken);

        var offers = skills.Select(skill =>
        {
            states.TryGetValue(skill.SkillId, out var state);
            var canReuse = state is not null && now < state.ExpiresAt;
            return new PlacementSkillOffer(
                skill.SkillId,
                skill.Code,
                skill.Name,
                skill.Modality,
                RequiresMicrophone: string.Equals(skill.Modality, Modality.Speaking.ToCode(), StringComparison.OrdinalIgnoreCase),
                RequiresAudio: string.Equals(skill.Modality, Modality.Listening.ToCode(), StringComparison.OrdinalIgnoreCase),
                Level: state?.CefrLevel,
                PositionInBand: state?.PositionInBand,
                MeasuredAt: state?.MeasuredAt,
                ExpiresAt: state?.ExpiresAt,
                CanReuse: canReuse);
        }).ToList();

        var cooldown = await ResolveCooldownAsync(userId, certId, now, cancellationToken);

        // The exam the start form posts. A certificate with no published placement paper has nothing to
        // start, and the screen says so rather than submitting an empty id.
        var examId = await db.Exams
            .AsNoTracking()
            .Where(e => e.CertId == certId
                && e.Purpose == ExamPurpose.Placement.ToCode()
                && e.Status == ExamStatus.Published.ToCode()
                && !e.IsDeleted)
            .OrderBy(e => e.ExamId)
            .Select(e => (int?)e.ExamId)
            .FirstOrDefaultAsync(cancellationToken);

        return new PlacementPreflightView(
            certId,
            certCode,
            examId,
            offers,
            cooldown.Until,
            cooldown.Waived,
            offers.Any(o => o.CanReuse));
    }

    /// <summary>
    /// Refuses a start that would be a reroll. The exam is resolved first by the caller, so an unknown
    /// exam still answers "not found" rather than "cooldown".
    /// </summary>
    private async Task EnsureCooldownElapsedAsync(int userId, int certId, CancellationToken cancellationToken)
    {
        var cooldown = await ResolveCooldownAsync(userId, certId, _clock.GetUtcNow(), cancellationToken);
        if (cooldown.Until is DateTimeOffset until)
        {
            throw new PlacementException(
                PlacementException.PlacementCooldown,
                $"A placement can be retaken every 24 hours; this one opens at {until:u}. There is no limit on how many you take.");
        }
    }

    /// <summary>
    /// The most recent finished sitting's cooldown. Only a sitting that measured NOTHING because every
    /// skill's grading failed waives it; see <see cref="PlacementCooldown"/> for why a skip and a
    /// timeout do not.
    /// <para>
    /// #217 / PLC-19: a sitting that measured nothing is now `abandoned` rather than `done`, so this
    /// read must include it. Leaving the filter at `done` would make the waiver unreachable and let a
    /// learner whose grading failed wait 24 hours for an outage that was not theirs.
    /// </para>
    /// </summary>
    private async Task<(DateTimeOffset? Until, bool Waived)> ResolveCooldownAsync(
        int userId, int certId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var previous = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CertId == certId
                && (p.Status == PlacementStatus.Done.ToCode() || p.Status == PlacementStatus.Abandoned.ToCode()))
            .OrderByDescending(p => p.TakenAt)
            .Select(p => new { p.PlacementId, p.TakenAt, p.CompletionFailureReason })
            .FirstOrDefaultAsync(cancellationToken);

        if (previous is null)
        {
            return (null, false);
        }

        // "Measured nothing because of an internal failure" is the whole waiver, and the skill rows are
        // what it is read from: the placement-level reason is only set when nothing was measured, and
        // the per-skill statuses are what tell a grading failure from a skip when both occur together.
        var statuses = await db.PlacementSkillResults
            .AsNoTracking()
            .Where(r => r.PlacementId == previous.PlacementId)
            .Select(r => new { r.ResultStatus, r.FailureReason })
            .ToListAsync(cancellationToken);

        var measured = statuses.Count(r =>
            r.ResultStatus is PlacementResultStatus.Measured or PlacementResultStatus.EstimatedFromOneTask);
        // #217: an abandoned sitting has NO per-skill rows at all (nothing was measured, so the writer
        // has nothing to say per skill) - the placement-level reason is then the only evidence, and it
        // is the same one a `done` sitting records.
        var systemFailure = measured == 0
            && (statuses.Count == 0
                ? previous.CompletionFailureReason == PlacementFailureReason.AiGradingFailed
                : statuses.All(r => r.ResultStatus == PlacementResultStatus.NotMeasured
                    && r.FailureReason == PlacementFailureReason.AiGradingFailed));

        return (
            PlacementCooldown.Until(previous.TakenAt, systemFailure, now),
            systemFailure && PlacementCooldown.Until(previous.TakenAt, false, now) is not null);
    }
}
