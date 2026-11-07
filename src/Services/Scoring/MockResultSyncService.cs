using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #222 / D-26 / OQ-TR-01: the mock result reads its own certificate score and a separately labelled
/// projected level, and never writes the learner's state on that path.
/// <para>
/// <b>Where the projection comes from.</b> Every level here is read out of the certificate's cut-score
/// table by <see cref="ICefrProjectionService.ProjectScoresAsync"/>. There is no arithmetic that turns
/// a band code into a level anywhere in this file, because that is exactly the claim PRN-03 removes:
/// TOEIC 785-940 is not CEFR B2.
/// </para>
/// <para>
/// <b>What "historical" means here.</b> A preserved attempt predates the cut-score table that would
/// project it. Rather than projecting it against today's table - which would invent a level nobody
/// measured at the time - the read looks for the newest version that already existed when the attempt
/// was submitted. When there is none, the result is marked historical and carries no projection at all.
/// </para>
/// </summary>
public sealed class MockResultSyncService(
    CompanionDbContext db,
    ICefrProjectionService projections,
    ISkillStateWriter skillStates) : IMockResultSyncService
{
    public async Task<MockResultProjection> GetResultAsync(
        int userId, int attemptId, CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .AsNoTracking()
            .Include(a => a.Exam)
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new MockExamException(
                MockExamException.AttemptNotFound, $"Mock attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.Graded.ToCode())
        {
            throw new MockExamException(
                MockExamException.AttemptNotFinished,
                "The projected level is available once the attempt is graded.");
        }

        var scores = await db.AttemptSkillScores
            .AsNoTracking()
            .Where(s => s.AttemptId == attemptId)
            .ToDictionaryAsync(s => s.SkillId, s => s.ScaledScore, cancellationToken);

        var bandName = attempt.BandCode is null
            ? null
            : await db.ScoreBands
                .AsNoTracking()
                .Where(b => b.CertId == attempt.CertId && b.Code == attempt.BandCode)
                .Select(b => b.Name)
                .FirstOrDefaultAsync(cancellationToken);

        var stored = await skillStates.ReadAsync(userId, attempt.CertId, cancellationToken);

        // The version in force when the attempt was submitted, not simply the newest: projecting an old
        // paper against a table published afterwards would state a level the learner was never measured
        // against. No such version means the attempt is historical and gets no projection.
        var submittedAt = attempt.SubmittedAt ?? attempt.StartedAt;
        var asOf = await db.CefrCutScoreVersions
            .AsNoTracking()
            .Where(v => v.CertId == attempt.CertId && v.PublishedAt <= DateOnly.FromDateTime(submittedAt.UtcDateTime))
            .OrderByDescending(v => v.TableVersion)
            .Select(v => (int?)v.TableVersion)
            .FirstOrDefaultAsync(cancellationToken);

        var historical = asOf is null || scores.Count == 0;
        var projected = historical
            ? null
            : await projections.ProjectScoresAsync(attempt.CertId, scores, asOf, cancellationToken);

        return new MockResultProjection(
            attempt.AttemptId,
            attempt.CertId,
            attempt.TotalScore ?? 0,
            attempt.BandCode,
            bandName,
            AttemptEngine.DisplayFactor(await AggregationRuleAsync(attempt.CertId, cancellationToken)),
            projected,
            stored,
            historical);
    }

    /// <summary>
    /// The explicit sync. It writes only skills the projection could judge and that differ from the
    /// stored state, and refuses when that set is empty - a button that writes nothing is harmless, but
    /// a button that writes the current value as a fresh measurement would silently re-date the
    /// learner's evidence.
    /// </summary>
    public async Task<LearnerStanding> SyncAsync(
        int userId, int attemptId, CancellationToken cancellationToken = default)
    {
        var result = await GetResultAsync(userId, attemptId, cancellationToken);

        if (result.Historical || result.Projected is null)
        {
            throw new MockExamException(
                MockExamException.NothingToSync,
                "This attempt has no projected level to sync: it predates the certificate's cut-score table.");
        }

        // A paper the table could not place as a whole writes nothing: syncing the half it could judge
        // would let one rushed section edit part of the learner's state, which is the defect the
        // explicit sync exists to prevent.
        if (result.Projected.Outcome == ProjectionOutcome.CannotJudge)
        {
            throw new MockExamException(
                MockExamException.NothingToSync,
                result.Projected.CannotJudgeReason is { } reason
                    ? $"Nothing to sync: this paper's scores could not be projected ({reason})."
                    : "Nothing to sync: this paper's scores could not be projected.");
        }

        var stored = result.Stored;
        var differing = result.Projected.SkillIdsDifferingFrom(stored)
            .ToHashSet();

        if (differing.Count == 0)
        {
            throw new MockExamException(
                MockExamException.NothingToSync,
                "Nothing to sync: every projected level already matches your stored level.");
        }

        foreach (var skill in result.Projected.Skills.Where(s => differing.Contains(s.SkillId)))
        {
            await skillStates.RecordAsync(
                userId,
                result.CertId,
                skill.SkillId,
                skill.AwardedLevel!.Value,
                skill.PositionInBand!.Value,
                skill.ContinuousCoordinate,
                LearnerSkillStateSource.MockSection,
                cancellationToken);
        }

        return await skillStates.ReadAsync(userId, result.CertId, cancellationToken)
            ?? throw new InvalidOperationException(
                "The per-skill state was synced but cannot be read back, which means the write did not take effect.");
    }

    private async Task<string> AggregationRuleAsync(int certId, CancellationToken cancellationToken) =>
        await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == certId)
            .Select(c => c.ScoreAggregationRule)
            .SingleAsync(cancellationToken);
}
