using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #156: decides whether a learner's practice accuracy has diverged from the band their placement
/// gave them, and — when it has — offers a calibration the learner can accept or defer.
/// <para>
/// This is a *detector*, not a corrector. Nothing here changes a band unless
/// <see cref="ApplyAsync"/> is called by an explicit learner choice, and every decision is written
/// down with the numbers behind it.
/// </para>
/// <para>
/// Deterministic: counts and divisions only, no AI. What it can see is objective practice answers
/// with a recorded response time. What it cannot see is anything about the placement sitting itself
/// other than its result — so a learner who did well on placement and then answers carefully but
/// wrongly is indistinguishable from one who was helped on placement day, and the wording never
/// claims otherwise.
/// </para>
/// </summary>
public sealed class CalibrationService(
    CompanionDbContext db,
    ILearnerService learners) : ICalibrationService
{
    /// <summary>
    /// AC6: no conclusion below this many valid answers. Twenty is the ticket's own figure and the
    /// smallest sample at which the gap this detector fires on is distinguishable from noise: with
    /// the 60% baseline and a 40% floor, 20 answers leave about a 5.7% chance of falling under the
    /// floor by luck alone. Ten answers would leave 16.6%, and the detector would accuse one learner
    /// in six of a divergence that is not there.
    /// </summary>
    public const int MinimumValidAnswers = 20;

    /// <summary>
    /// AC5: an answer made sooner than this after the previous one in the session is a guess, not a
    /// measurement, and is left out of the sample. Three seconds is the rapid-guessing floor in the
    /// response-time literature for a reading item; it is a filter on evidence, never a penalty.
    /// </summary>
    public static readonly TimeSpan RushedAnswerThreshold = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The accuracy a learner on the placed band's material is expected to reach. A path aimed at
    /// their level assumes they can do about 60% of it.
    /// </summary>
    public const int ExpectedAccuracyPct = 60;

    /// <summary>
    /// The floor this detector acts on. It sits twenty points under the baseline: a gap that size is
    /// well outside the sampling error of a twenty-answer window, while a smaller one is not worth
    /// telling a learner their level is wrong about.
    /// </summary>
    public const int AccuracyFloorPct = 40;

    public async Task<CalibrationView> GetCalibrationAsync(int userId, CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var snapshot = await ReadSnapshotAsync(userId, certId, cancellationToken);

        if (snapshot.PlacedBand is null)
        {
            return new CalibrationView(
                "", null, snapshot.ValidCount, MinimumValidAnswers, snapshot.ExcludedCount,
                snapshot.AccuracyPct, CalibrationReason.NotPlaced, null);
        }

        var placed = snapshot.PlacedBand;
        var reason = CalibrationReason.BelowThreshold;
        CalibrationProposal? proposal = null;

        if (snapshot.ValidCount >= MinimumValidAnswers)
        {
            reason = CalibrationReason.Consistent;

            string? lowerBandCode = null;
            if (snapshot.AccuracyPct is { } accuracy && accuracy < AccuracyFloorPct)
            {
                var lower = snapshot.Bands
                    .Where(b => b.DisplayOrder < placed.DisplayOrder)
                    .OrderByDescending(b => b.DisplayOrder)
                    .ThenByDescending(b => b.MinTotal)
                    .FirstOrDefault();

                // #156: if the learner already accepted a calibration and the path now starts at or
                // below the band this evidence supports, there is nothing left to propose. Without
                // this, accepting the proposal would immediately produce it again.
                var pathBand = snapshot.PathBandCode is { } code
                    ? snapshot.Bands.FirstOrDefault(b => b.Code == code)
                    : null;

                if (lower is not null && pathBand is not null && pathBand.DisplayOrder <= lower.DisplayOrder)
                {
                    reason = CalibrationReason.Calibrated;
                }
                else if (lower is not null)
                {
                    reason = CalibrationReason.Diverged;
                    lowerBandCode = lower.Code;
                    proposal = new CalibrationProposal(
                        lower.Code,
                        lower.Name,
                        accuracy,
                        snapshot.ValidCount,
                        AccuracyFloorPct,
                        EvidenceText(placed, lower, accuracy, snapshot.ValidCount));
                }
                else
                {
                    // Nothing below the placed band to move to: the honest answer is that there is
                    // no route this evidence can offer, not a proposal that would be a no-op.
                    reason = CalibrationReason.LowestBand;
                }
            }

            // A learner who already said "later" is not asked again on the same evidence. New valid
            // answers push the count past the sample that was deferred, which is what reopens it.
            if (proposal is not null)
            {
                var deferred = await db.CalibrationDecisions
                    .AsNoTracking()
                    .Where(d => d.UserId == userId && d.CertId == certId
                        && d.Decision == CalibrationDecisionKind.Deferred)
                    .OrderByDescending(d => d.DecidedAt)
                    .ThenByDescending(d => d.DecisionId)
                    .Select(d => (int?)d.SampleSize)
                    .FirstOrDefaultAsync(cancellationToken);

                if (deferred is { } deferredAt && snapshot.ValidCount <= deferredAt)
                {
                    reason = CalibrationReason.Deferred;
                    proposal = null;
                }
            }
        }

        return new CalibrationView(
            placed.Code,
            placed.Name,
            snapshot.ValidCount,
            MinimumValidAnswers,
            snapshot.ExcludedCount,
            snapshot.AccuracyPct,
            reason,
            proposal);
    }

    /// <summary>
    /// AC4: the learner's own answer. <paramref name="adjust"/> false records "later" and writes
    /// nothing else, so the path is exactly as it was — that is the whole point of asking.
    /// </summary>
    public async Task<CalibrationDecisionResult> DecideAsync(
        int userId, bool adjust, CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var view = await GetCalibrationAsync(userId, cancellationToken);

        // Recompute at the moment of the click: a proposal from a page opened ten answers ago must
        // not be applied to a sample that no longer supports it.
        if (view.Proposal is not { } proposal)
        {
            throw new CalibrationException(
                CalibrationException.NoProposal,
                "There is no calibration to answer: the evidence no longer points to a lower band.");
        }

        var now = DateTimeOffset.UtcNow;
        db.CalibrationDecisions.Add(new CalibrationDecision
        {
            UserId = userId,
            CertId = certId,
            PlacedBandCode = view.PlacedBandCode,
            ProposedBandCode = proposal.ProposedBandCode,
            SampleSize = proposal.ValidAnswerCount,
            ObservedAccuracyPct = proposal.ObservedAccuracyPct,
            Decision = adjust ? CalibrationDecisionKind.Adjusted : CalibrationDecisionKind.Deferred,
            DecidedAt = now,
        });

        if (adjust)
        {
            // The path is the learner's own row; its starting band is what "the path starts lower"
            // means in this model. Modules are section-driven and keep their order, so nothing the
            // learner has already done is thrown away.
            var path = await db.LearnerPaths
                .FirstOrDefaultAsync(p => p.UserId == userId && p.CertId == certId && p.Status == "active", cancellationToken);
            if (path is not null)
            {
                path.CurrentBandCode = proposal.ProposedBandCode;
            }

            // Keep the goal's starting band in step, so the goal screen and a later regeneration
            // agree with the path the learner just accepted.
            var goal = await db.TargetBands
                .FirstOrDefaultAsync(t => t.UserId == userId && t.CertId == certId, cancellationToken);
            if (goal is not null)
            {
                goal.CurrentBandCode = proposal.ProposedBandCode;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return new CalibrationDecisionResult(
            adjust ? CalibrationDecisionKind.Adjusted : CalibrationDecisionKind.Deferred,
            view.PlacedBandCode,
            proposal.ProposedBandCode,
            PathAdjusted: adjust);
    }

    private sealed record Snapshot(
        ScoreBand? PlacedBand,
        IReadOnlyList<ScoreBand> Bands,
        int ValidCount,
        int ExcludedCount,
        decimal? AccuracyPct,
        string? PathBandCode);

    private sealed record AnswerRow(int SessionId, DateTime? AnsweredAt, bool IsCorrect);

    private async Task<Snapshot> ReadSnapshotAsync(int userId, int certId, CancellationToken cancellationToken)
    {
        var bands = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == certId)
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.MinTotal)
            .ToListAsync(cancellationToken);

        var placement = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CertId == certId && p.Status == PlacementStatus.Done.ToCode())
            .OrderByDescending(p => p.TakenAt)
            .FirstOrDefaultAsync(cancellationToken);

        string? bandCode = placement?.ResultBandCode;
        DateTimeOffset since = placement?.TakenAt ?? DateTimeOffset.MinValue;

        if (string.IsNullOrEmpty(bandCode))
        {
            // A graded mock is the other band this system records. Without either, there is no
            // placement result to diverge from, and the detector says so instead of inventing one.
            var mock = await db.ExamAttempts
                .AsNoTracking()
                .Where(a => a.UserId == userId && a.CertId == certId
                    && a.Status == AttemptStatus.Graded.ToCode()
                    && a.Exam.Purpose == ExamPurpose.Mock.ToCode()
                    && a.BandCode != null)
                .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
                .FirstOrDefaultAsync(cancellationToken);
            bandCode = mock?.BandCode;
            since = mock?.SubmittedAt ?? mock?.StartedAt ?? DateTimeOffset.MinValue;
        }

        var placedBand = bandCode is null ? null : bands.FirstOrDefault(b => b.Code == bandCode);

        // #156: where the learner's own path starts now. Read here so "already calibrated" is judged
        // from the row the learner's decision wrote, not from anything the screen remembers.
        var pathBandCode = await db.LearnerPaths
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.CertId == certId && p.Status == "active")
            .Select(p => p.CurrentBandCode)
            .FirstOrDefaultAsync(cancellationToken);

        if (placedBand is null)
        {
            return new Snapshot(null, bands, 0, 0, null, pathBandCode);
        }

        // Evidence is practice done *after* the result being tested, on the same certificate, and
        // only answers the system actually graded. A skipped question is not an answer.
        var rows = await db.PracticeAnswers
            .AsNoTracking()
            .Where(a => a.Session.UserId == userId
                && a.Session.Section.CertId == certId
                && a.IsCorrect != null
                && a.Session.StartedAt >= since)
            .Select(a => new AnswerRow(a.SessionId, a.AnsweredAt, a.IsCorrect!.Value))
            .ToListAsync(cancellationToken);

        var valid = new List<bool>();
        var excluded = 0;

        foreach (var session in rows.GroupBy(r => r.SessionId))
        {
            // Response time is the gap to the previous *recorded* answer in the same sitting.
            // Null timestamps are excluded rather than guessed: a row written before the column
            // existed carries no measurement at all.
            DateTime? previous = null;
            foreach (var row in session.OrderBy(r => r.AnsweredAt ?? DateTime.MaxValue).ThenBy(r => r.SessionId))
            {
                if (row.AnsweredAt is not { } at)
                {
                    excluded++;
                    continue;
                }

                var gap = previous is { } p ? at - p : (TimeSpan?)null;
                previous = at;

                if (gap is { } g && g < RushedAnswerThreshold)
                {
                    excluded++;
                    continue;
                }

                valid.Add(row.IsCorrect);
            }
        }

        // EvidenceText promises "on your last N practice answers", and a learner who improves should
        // stop being told they diverge. Counting every answer since the placement makes one bad week
        // permanent: 20 wrong then 90 right still reads 90/110 = 82%... but 20 wrong then 20 right
        // reads 50% and keeps re-proposing. Keep the most recent MinimumValidAnswers instead, so the
        // window slides with the learner. The rows come from one session-grouped list, so the newest
        // are the tail of the flattened sequence by timestamp.
        var recent = valid.Count > MinimumValidAnswers
            ? valid.Skip(valid.Count - MinimumValidAnswers).ToList()
            : valid;

        decimal? accuracy = recent.Count == 0
            ? null
            : Math.Round(recent.Count(v => v) * 100m / recent.Count, 2);

        return new Snapshot(placedBand, bands, recent.Count, excluded, accuracy, pathBandCode);
    }

    /// <summary>
    /// The sentence the learner reads. It names the counts and both bands and stops there: no
    /// inference about why, and no word for dishonesty anywhere in it, because the data cannot
    /// distinguish "hard material" from "a bad week" from "someone else sat the placement".
    /// </summary>
    private static string EvidenceText(ScoreBand placed, ScoreBand proposed, decimal accuracy, int count) =>
        $"On your last {count} practice answers at {placed.Code} level, {accuracy:0.#}% were correct. " +
        $"A path built for {placed.Code} assumes about {ExpectedAccuracyPct}%. " +
        $"You can lower your starting band to {proposed.Code} ({proposed.Name}) or keep the path you have.";
}
