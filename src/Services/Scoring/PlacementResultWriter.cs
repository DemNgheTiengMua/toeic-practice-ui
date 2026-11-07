using BusinessObjects;
using BusinessObjects.Entities;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #212: the one writer of a placement's per-skill results, and the derivation that decides each
/// skill's status from what the sitting actually produced.
/// <para>
/// <b>Why a status is derived rather than set.</b> A caller that passed in
/// <see cref="PlacementResultStatus.Measured"/> and nothing else would produce rows the schema accepts
/// and the product cannot tell apart from a real measurement — every row "measured", no test the
/// wiser. Everything here reads the attempt: which skills the paper examined, whether each was
/// attempted, whether its productive tasks graded, and whether the table could place its score. The
/// status is the conclusion of that reading, not an argument.
/// </para>
/// <para>
/// <b>The cut-score version is resolved ONCE, before the rows, and the same value is passed to the
/// projection and stored on the row.</b> #222 had to resolve a version at read time because
/// <c>ExamAttempts</c> carries none, and recorded that as a reproducibility risk. A row that stores a
/// version must have been projected against that version; passing one value to both is what makes the
/// two unable to describe different tables.
/// </para>
/// <para>
/// <b>A skill still in the AI queue is not a failure.</b> A productive skill with a pending grading is
/// <see cref="PlacementResultStatus.Grading"/> with no failure reason; only a skill whose every task
/// finished unsuccessfully is <see cref="PlacementResultStatus.NotMeasured"/> with
/// <c>ai_grading_failed</c>. Collapsing the two is the defect this table was built to remove.
/// </para>
/// </summary>
public sealed class PlacementResultWriter(
    CompanionDbContext db,
    ICefrProjectionService? projections = null,
    ISkillStateWriter? skillStates = null,
    TimeProvider? clock = null) : IPlacementResultWriter
{
    /// <summary>D18: the same 90-day reuse window a stored per-skill state uses.</summary>
    public static readonly TimeSpan ReuseWindow = SkillStateWriter.ReuseWindow;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    // Defaulted rather than required, following AiGradingProcessor's `attemptEngine ?? new
    // AttemptEngine(db)` convention: AttemptEngine constructs this writer with nothing but its own
    // context, and the container's registrations are what a host that wants to substitute them uses.
    private readonly ICefrProjectionService _projections = projections ?? new CefrProjectionService(db);
    private readonly ISkillStateWriter _skillStates = skillStates ?? new SkillStateWriter(db, clock);

    /// <inheritdoc />
    public async Task<PlacementWriteOutcome> WriteAsync(
        int placementId, CancellationToken cancellationToken = default)
    {
        var placement = await db.PlacementTests
            .Include(p => p.ExamAttempt).ThenInclude(a => a!.Exam)
            .SingleOrDefaultAsync(p => p.PlacementId == placementId, cancellationToken)
            ?? throw new PlacementException(
                PlacementException.PlacementNotFound, $"Placement test {placementId} not found.");

        if (placement.Status == PlacementStatus.Abandoned.ToCode())
        {
            return new PlacementWriteOutcome(placementId, false, false, 0, null);
        }

        var attempt = placement.ExamAttempt
            ?? throw new PlacementException(
                PlacementException.PlacementNotFound,
                $"Exam attempt for placement test {placementId} not found.");

        var now = _clock.GetUtcNow();

        // The certificate's examined skills, in its own order. A placement reports on the skills the
        // certificate has, never on a fixed four (D4): TOEIC CAT examines two.
        var skills = await db.Skills
            .AsNoTracking()
            .Where(s => s.CertId == placement.CertId)
            .OrderBy(s => s.DisplayOrder).ThenBy(s => s.SkillId)
            .Select(s => s.SkillId)
            .ToListAsync(cancellationToken);

        // What the paper actually scored, and whether each skill was attempted at all. The engine
        // pre-creates an empty AttemptAnswers row per objective question when the sitting starts, so a
        // row is not evidence of an answer: only a selected option is (the same rule the review page
        // and the answered counter use).
        var scored = await db.AttemptSkillScores
            .AsNoTracking()
            .Where(s => s.AttemptId == attempt.AttemptId)
            .ToDictionaryAsync(s => s.SkillId, s => s.ScaledScore, cancellationToken);

        var answeredBySkill = await db.AttemptAnswers
            .AsNoTracking()
            .Where(a => a.AttemptId == attempt.AttemptId
                && (a.SelectedOptionId != null || a.SelectedOptionIdsJson != null || a.ResponseText != null))
            .GroupBy(a => a.Question.Section.SkillId)
            .Select(g => new { SkillId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.SkillId, g => g.Count, cancellationToken);

        var gradingsBySkill = (await db.AiGradings
                .AsNoTracking()
                .Where(g => g.FreeResponse.AttemptId == attempt.AttemptId)
                .Select(g => new { g.SkillId, g.Status })
                .ToListAsync(cancellationToken))
            .ToLookup(g => g.SkillId, g => g.Status);

        // The version is resolved ONCE. Null means the certificate publishes no table yet, which makes
        // every projection a null triple rather than a number nobody can reproduce.
        var tableVersion = await db.CefrCutScoreVersions
            .AsNoTracking()
            .Where(v => v.CertId == placement.CertId)
            .OrderByDescending(v => v.TableVersion)
            .Select(v => (int?)v.TableVersion)
            .FirstOrDefaultAsync(cancellationToken);

        // The projection is the score-to-level direction #222 shipped: the cut-score table and nothing
        // else decides a projected level (LVL-12). It is read, not recomputed.
        var projectionsBySkill = new Dictionary<int, ScaledScoreSkillProjection>();
        var judgeable = scored.Where(s => skills.Contains(s.Key)).ToDictionary(s => s.Key, s => s.Value);
        if (judgeable.Count > 0 && tableVersion is not null)
        {
            var projection = await _projections.ProjectScoresAsync(
                placement.CertId, judgeable, tableVersion, cancellationToken);
            foreach (var skill in projection.Skills)
            {
                projectionsBySkill[skill.SkillId] = skill;
            }
        }

        var bands = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == placement.CertId)
            .OrderBy(b => b.MinTotal)
            .ToListAsync(cancellationToken);

        var rows = await db.PlacementSkillResults
            .Where(r => r.PlacementId == placementId)
            .ToDictionaryAsync(r => r.SkillId, cancellationToken);

        // Nothing attempted and nothing queued on an auto-submitted sitting is a timeout, not a skip:
        // the distinction is the whole reason the failure reason exists.
        var sittingTimedOut = attempt.IsAutoSubmitted
            && answeredBySkill.Count == 0
            && !gradingsBySkill.Any();
        var measured = 0;
        var grading = 0;
        string? firstFailure = null;

        foreach (var skillId in skills)
        {
            var status = DeriveStatus(
                skillId, scored, answeredBySkill, gradingsBySkill, sittingTimedOut, out var failureReason);

            var row = rows.GetValueOrDefault(skillId);
            var isNew = row is null;
            if (isNew)
            {
                row = new PlacementSkillResult { PlacementId = placementId, SkillId = skillId };
                db.PlacementSkillResults.Add(row);
            }

            var projected = projectionsBySkill.GetValueOrDefault(skillId);
            var measuredNow = status is PlacementResultStatus.Measured
                or PlacementResultStatus.EstimatedFromOneTask;

            row!.CertId = placement.CertId;
            row.ResultStatus = status;
            row.IsEstimatedFromOneTask = status == PlacementResultStatus.EstimatedFromOneTask;

            // The declaration is set BEFORE anything that can save this row. SkillStateWriter's
            // RecordAsync calls SaveChanges on the shared context, and that flush would persist a row
            // whose WrittenByService is still null - refused by the trigger, for a reason that looks
            // like a second writer rather than an ordering mistake.
            row.WrittenByService = DerivedFactWriters.PlacementSkillResult;
            db.Entry(row).Property(r => r.WrittenByService).IsModified = true;
            row.ProjectionVersion = isNew ? 1 : row.ProjectionVersion + 1;

            if (measuredNow)
            {
                // The projection owns the level when it could place the score. When it could not - a
                // gap between two cut ranges, or no table published - the attempt's own band is the
                // only evidence there is, and reading it is honest where inventing A1 would not be.
                var level = projected?.AwardedLevel ?? LevelFromBand(attempt.BandCode);
                var position = projected?.PositionInBand
                    ?? PositionFromBand(attempt.TotalScore, attempt.BandCode, bands);
                var coordinate = projected?.ContinuousCoordinate ?? CoordinateFromLevel(level);

                row.CefrLevel = level;
                row.PositionInBand = position;
                row.ContinuousCoordinate = coordinate;
                row.ProjectedBandCode = projected is null ? null : BandCodeFor(attempt.TotalScore, bands);
                row.ProjectedScore = projected is null ? null : attempt.TotalScore;
                row.CutScoreTableVersion = projected is null ? null : tableVersion;
                row.MeasuredAt = now;
                row.ExpiresAt = now + ReuseWindow;
                row.FailureReason = null;
                // #214 owns the 70%-rule pass that can truncate a level; nothing here has per-band
                // correctness to truncate from, so the flag is set where that evidence exists.
                row.IsTruncated = false;
                measured++;

                // The same numbers feed the learner's stored state, so the dashboard and this result
                // cannot report different levels for one sitting.
                await _skillStates.RecordAsync(
                    placement.UserId, placement.CertId, skillId,
                    level, position, coordinate,
                    LearnerSkillStateSource.Placement, cancellationToken);
            }
            else
            {
                row.CefrLevel = null;
                row.PositionInBand = null;
                row.ContinuousCoordinate = null;
                row.ProjectedBandCode = null;
                row.ProjectedScore = null;
                row.CutScoreTableVersion = null;
                row.MeasuredAt = null;
                row.ExpiresAt = null;
                row.FailureReason = failureReason;
                row.IsTruncated = false;

                if (status == PlacementResultStatus.Grading) grading++;
                else firstFailure ??= failureReason;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // Completion, decided after the rows exist. A productive task still in the queue keeps the
        // placement in progress (PLC-16); nothing measured and nothing queued is a recorded completion
        // failure, which is the honest alternative to "done with a null result".
        var completionFailure = measured == 0 && grading == 0
            ? (sittingTimedOut
                ? PlacementFailureReason.InactivityTimeout
                : firstFailure ?? PlacementFailureReason.CandidateSkipped)
            : null;

        var placementRow = await db.PlacementTests
            .SingleAsync(p => p.PlacementId == placementId, cancellationToken);

        if (grading > 0)
        {
            placementRow.Status = PlacementStatus.InProgress.ToCode();
            placementRow.CompletionFailureReason = null;
        }
        else if (measured > 0 || completionFailure is not null)
        {
            placementRow.Status = PlacementStatus.Done.ToCode();
            placementRow.CompletionFailureReason = completionFailure;
        }

        // The snapshot fields mirror the attempt, so a screen showing "385 out of 990" traces back to
        // the attempt rather than to a second computation (the M-2 invariant).
        placementRow.TotalScore = attempt.TotalScore;
        placementRow.ResultBandCode = attempt.BandCode;
        placementRow.TakenAt = attempt.SubmittedAt ?? placementRow.TakenAt;
        placementRow.WrittenByService = DerivedFactWriters.PlacementResult;
        db.Entry(placementRow).Property(p => p.WrittenByService).IsModified = true;
        placementRow.ProjectionVersion++;

        await db.SaveChangesAsync(cancellationToken);

        return new PlacementWriteOutcome(
            placementId,
            IsDone: placementRow.Status == PlacementStatus.Done.ToCode(),
            IsGrading: grading > 0,
            MeasuredSkillCount: measured,
            CompletionFailureReason: completionFailure);
    }

    /// <summary>
    /// Reads one skill's outcome from what the sitting produced. The order matters: a graded productive
    /// task outranks a timed-out sitting, because the learner did answer it.
    /// </summary>
    private static PlacementResultStatus DeriveStatus(
        int skillId,
        IReadOnlyDictionary<int, int> scored,
        IReadOnlyDictionary<int, int> answeredBySkill,
        ILookup<int, string> gradingsBySkill,
        bool sittingTimedOut,
        out string? failureReason)
    {
        failureReason = null;
        var gradings = gradingsBySkill[skillId].ToList();

        // A productive skill's status is the grading queue's, not the objective score's.
        if (gradings.Count > 0)
        {
            if (gradings.Any(g => g == AiGradingStatus.Pending.ToCode()))
            {
                return PlacementResultStatus.Grading;
            }

            var done = gradings.Count(g => g == AiGradingStatus.Done.ToCode());
            // D7: every task the paper delivered graded is a full measurement; fewer than that but at
            // least one is "estimated from one task". The denominator is what the paper DELIVERED
            // (one AiGrading per free response), never a hardcoded two - D6's two tasks is a paper
            // design, and a certificate whose paper carries one task would otherwise never be measured.
            if (done == gradings.Count) return PlacementResultStatus.Measured;
            if (done >= 1) return PlacementResultStatus.EstimatedFromOneTask;

            // Every task failed. PLC-35: not_measured with a reason and a retry, never A1.
            failureReason = PlacementFailureReason.AiGradingFailed;
            return PlacementResultStatus.NotMeasured;
        }

        // An objective skill with a stored score and at least one answer is measured. This is what the
        // attempt's own scoring pass already decided; deciding again here would be a second opinion.
        if (scored.ContainsKey(skillId) && answeredBySkill.GetValueOrDefault(skillId) > 0)
        {
            return PlacementResultStatus.Measured;
        }

        failureReason = sittingTimedOut
            ? PlacementFailureReason.InactivityTimeout
            : PlacementFailureReason.CandidateSkipped;
        return PlacementResultStatus.NotMeasured;
    }

    private static string? BandCodeFor(int? totalScore, IReadOnlyList<ScoreBand> bands) =>
        totalScore is int total ? AttemptEngine.MatchBandCode(total, bands) : null;

    /// <summary>
    /// The level to report when the cut-score table could not place this skill's score. A band code
    /// that is also a reportable level name (IELTS "B2", TOEIC "A2") is the certificate's own label for
    /// it; a code like "below_A1" or "CAT_B1" is not a level, so it reports the pre-A1 floor rather
    /// than inventing one (PRN-03).
    /// </summary>
    private static CefrLevel LevelFromBand(string? bandCode) =>
        CefrLevel.TryParse(bandCode, out var level) ? level : CefrLevel.PreA1;

    private static int PositionFromBand(int? totalScore, string? bandCode, IReadOnlyList<ScoreBand> bands)
    {
        if (totalScore is not int total
            || bands.FirstOrDefault(b => b.Code == bandCode) is not { } band
            || band.MaxTotal <= band.MinTotal)
        {
            return 0;
        }

        var position = (int)Math.Round(
            (total - band.MinTotal) * 100m / (band.MaxTotal - band.MinTotal), MidpointRounding.AwayFromZero);
        return Math.Clamp(position, 0, 100);
    }

    private static decimal CoordinateFromLevel(CefrLevel level) =>
        level.IsReportableFloor
            ? 1.00m
            : Math.Clamp((decimal)level.Rank, 1.00m, 6.99m);
}
