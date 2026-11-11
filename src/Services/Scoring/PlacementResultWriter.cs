using BusinessObjects;
using BusinessObjects.Entities;
using Contracts;
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

        // #269: a skill comes from the answer's own row - the roster position on a pool paper, the
        // legacy question otherwise - never from a navigation that is null on half the rows.
        var skillByAnswer = await RosterSkills.ByAnswerAsync(db, attempt.AttemptId, cancellationToken);

        var answeredBySkill = (await db.AttemptAnswers
                .AsNoTracking()
                .Where(a => a.AttemptId == attempt.AttemptId
                    && (a.SelectedOptionId != null || a.SelectedLabel != null
                        || a.SelectedOptionIdsJson != null || a.ResponseText != null))
                .Select(a => a.AnswerId)
                .ToListAsync(cancellationToken))
            .Where(id => skillByAnswer.ContainsKey(id))
            .GroupBy(id => skillByAnswer[id])
            .ToDictionary(g => g.Key, g => g.Count());

        var gradingsBySkill = (await db.AiGradings
                .AsNoTracking()
                .Where(g => g.FreeResponse.AttemptId == attempt.AttemptId)
                .Select(g => new { g.SkillId, g.Status })
                .ToListAsync(cancellationToken))
            .ToLookup(g => g.SkillId, g => g.Status);

        // #217 / PLC-35: every task the paper DELIVERED, per skill - a free response with no grading row
        // is a task that was settled without being scored (a blank one, or one whose failure left no
        // row). D7 divides by this, so "one of two tasks graded" stays an estimate and a blank task does
        // not quietly become a full measurement.
        var deliveredBySkill = (await db.FreeResponses
                .AsNoTracking()
                .Where(fr => fr.AttemptId == attempt.AttemptId)
                .Select(fr => new { fr.SkillId })
                .ToListAsync(cancellationToken))
            .GroupBy(fr => fr.SkillId)
            .ToDictionary(g => g.Key, g => g.Count());

        // #219 / D5: what the sitting SAW, per (skill, topic, band). Read from the answers themselves
        // rather than from AttemptSkillScores, because a topic is a property of the item the learner
        // answered and the aggregates carry no topic. An answer with no pool pin has no topic to name
        // (the pool assembled this paper after the sitting, or never), so it contributes nothing
        // rather than an invented one.
        var topicPriorsBySkill = await LoadTopicPriorsAsync(attempt.AttemptId, cancellationToken);

        // #219 / D5: the productive half's rubric profile, so the path starts from a named weakest
        // criterion instead of recomputing it from the grading rows every time it plans.
        var criterionProfilesBySkill = await LoadCriterionProfilesAsync(attempt.AttemptId, cancellationToken);

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

        // #213 / D18: the skills a preflight chose to carry over. The window is the STORED one: a state
        // whose ExpiresAt has passed is not reusable, and a state that is still valid is copied verbatim -
        // including its original MeasuredAt, which is what the badge on the result screen prints.
        var reusable = placement.ReusesPriorResults
            ? await db.LearnerSkillStates
                .AsNoTracking()
                .Where(s => s.UserId == placement.UserId && s.CertId == placement.CertId
                    && s.ExpiresAt > now)
                .ToDictionaryAsync(s => s.SkillId, cancellationToken)
            : [];

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
            var row = rows.GetValueOrDefault(skillId);
            var isNew = row is null;
            if (isNew)
            {
                row = new PlacementSkillResult { PlacementId = placementId, SkillId = skillId };
                db.PlacementSkillResults.Add(row);
            }

            row!.CertId = placement.CertId;

            // The declaration is set BEFORE anything that can save this row. SkillStateWriter's
            // RecordAsync calls SaveChanges on the shared context, and that flush would persist a row
            // whose WrittenByService is still null - refused by the trigger, for a reason that looks
            // like a second writer rather than an ordering mistake.
            row.WrittenByService = DerivedFactWriters.PlacementSkillResult;
            db.Entry(row).Property(r => r.WrittenByService).IsModified = true;
            row.ProjectionVersion = isNew ? 1 : row.ProjectionVersion + 1;

            // #213 / story 51-55: a carried skill FILLS A GAP, it does not overwrite evidence. It applies
            // only where this sitting did not measure the skill itself - a paper that does not reach it,
            // or one the learner stopped before. Carrying over a skill that was actually measured would
            // discard the fresh result and leave LearnerSkillStates stale, which is a silent data loss.
            //
            // Its dates are the STORED ones, copied verbatim: re-stamping MeasuredAt would move the
            // measurement to today (and CK_PSR_Expiry refuses it), which is the defect the badge exists
            // to make visible. Nothing is written back to LearnerSkillStates - reuse derives no new fact,
            // so the single writer of that table must not be called.
            var status = DeriveStatus(
                skillId,
                scored,
                answeredBySkill,
                gradingsBySkill,
                deliveredBySkill,
                sittingTimedOut,
                out var failureReason);

            var projected = projectionsBySkill.GetValueOrDefault(skillId);
            var measuredNow = status is PlacementResultStatus.Measured
                or PlacementResultStatus.EstimatedFromOneTask;

            if (!measuredNow && reusable.TryGetValue(skillId, out var carried))
            {
                row.ResultStatus = PlacementResultStatus.Measured;
                row.IsEstimatedFromOneTask = false;
                row.CefrLevel = carried.CefrLevel;
                row.PositionInBand = carried.PositionInBand;
                row.ContinuousCoordinate = carried.ContinuousCoordinate;
                row.ProjectedBandCode = null;
                row.ProjectedScore = null;
                row.CutScoreTableVersion = null;
                row.MeasuredAt = carried.MeasuredAt;
                row.ExpiresAt = carried.ExpiresAt;
                row.FailureReason = null;
                row.IsTruncated = false;
                row.ReusedFromPrior = true;
                // #219: no topic or criterion signal is set here, and none needs clearing. This branch
                // runs only where the sitting measured nothing, so the row was either just created
                // (null by construction) or written by the not-measured branch below, which nulls
                // both. Copying the earlier hint forward would present last month's observation as
                // this sitting's - the re-stamping defect MeasuredAt avoids.
                measured++;
                continue;
            }

            row.ResultStatus = status;
            row.IsEstimatedFromOneTask = status == PlacementResultStatus.EstimatedFromOneTask;
            row.ReusedFromPrior = false;

            if (measuredNow)
            {
                // #259 / PRN-03: the projection owns the level, and only the projection. When it could
                // not place the score — a gap between two cut ranges, or no table at all — there IS no
                // level, and the band's own name is not one.
                if (projected?.AwardedLevel is not { } level)
                {
                    RecordUnplaceable(row);
                    // A gap is a content fact, not a learner outcome, so it is NOT a completion failure:
                    // the placement can still be a partial result on its other skills.
                    firstFailure ??= PlacementFailureReason.NoCutScoreRow;
                    continue;
                }

                var position = projected.PositionInBand!.Value;
                var coordinate = projected.ContinuousCoordinate;

                row.CefrLevel = level;
                row.PositionInBand = position;
                row.ContinuousCoordinate = coordinate;
                row.ProjectedBandCode = BandCodeFor(attempt.TotalScore, bands);
                row.ProjectedScore = attempt.TotalScore;
                row.CutScoreTableVersion = tableVersion;
                row.MeasuredAt = now;
                row.ExpiresAt = now + ReuseWindow;
                row.FailureReason = null;
                // #219 / D5: the sequencing hint and the rubric profile. Both are nullable: a paper
                // that carried no pool pin has no topic to name, and an objective skill has no rubric.
                row.TopicPriorsJson = PlacementTopicPrior.Serialize(
                    topicPriorsBySkill.GetValueOrDefault(skillId) ?? []);
                row.CriterionProfileJson = PlacementCriterionProfile.Serialize(
                    criterionProfilesBySkill.GetValueOrDefault(skillId));
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
                // #219: nothing measured means nothing was read from it, so it contributes no hint.
                row.TopicPriorsJson = null;
                row.CriterionProfileJson = null;

                if (status == PlacementResultStatus.Grading) grading++;
                else firstFailure ??= failureReason;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // Completion, decided after the rows exist. A productive task still in the queue keeps the
        // placement in progress (PLC-16); nothing measured and nothing queued is a sitting that
        // produced no result at all.
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
        else if (measured > 0)
        {
            // #217 / PLC-19: a sitting where at least one skill was measured is a PARTIAL result. The
            // unattempted or failed skills are already `not_measured` with a reason, and they were never
            // given a level or an attempt score, so they are absent from the overall rather than
            // present in it at zero. `done` is legal here because a measured row exists - which is
            // exactly what trg_PlacementTests_CompletionIsEarned asks.
            placementRow.Status = PlacementStatus.Done.ToCode();
            placementRow.CompletionFailureReason = null;
        }
        else
        {
            // #217 / PLC-19, criterion 3: nothing could be measured, so there is NO result. `done` with
            // a failure reason was #212's shape and it still presented a result page whose headline was
            // a null overall; the ticket asks for the sitting to be recorded as abandoned instead. The
            // reason is kept on the row because it is the honest record of why, and the cooldown waiver
            // (and the retry) read it.
            //
            // A `done` row is what trg_PlacementTests_CompletionIsEarned guards, so this branch cannot
            // fight the guard #212 built: it is not `done`, and it claims no measurement.
            placementRow.Status = PlacementStatus.Abandoned.ToCode();
            placementRow.CompletionFailureReason = completionFailure;
        }

        // The snapshot fields mirror the attempt, so a screen showing "385 out of 990" traces back to
        // the attempt rather than to a second computation (the M-2 invariant). A sitting that measured
        // nothing has no total and no band on the attempt either, so this copies NULL rather than a
        // stale or fabricated figure.
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
        IReadOnlyDictionary<int, int> deliveredBySkill,
        bool sittingTimedOut,
        out string? failureReason)
    {
        failureReason = null;
        var gradings = gradingsBySkill[skillId].ToList();
        var delivered = deliveredBySkill.GetValueOrDefault(skillId);

        // A productive skill's status is the grading queue's, not the objective score's.
        if (delivered > 0)
        {
            if (gradings.Any(g => g == AiGradingStatus.Pending.ToCode()))
            {
                return PlacementResultStatus.Grading;
            }

            var done = gradings.Count(g => g == AiGradingStatus.Done.ToCode());
            // D7: every task the paper delivered graded is a full measurement; fewer than that but at
            // least one is "estimated from one task". The denominator is what the paper DELIVERED
            // (every free-response row, whether it carries a grading or was settled blank), never a
            // hardcoded two - D6's two tasks is a paper design, and a certificate whose paper carries
            // one task would otherwise never be measured. #217: counting the GRADING ROWS instead made
            // a blank task silently turn an estimate into a full measurement.
            if (done == delivered) return PlacementResultStatus.Measured;
            if (done >= 1) return PlacementResultStatus.EstimatedFromOneTask;

            // Nothing graded at all. A task that was settled blank is a skip; a task whose grading
            // FAILED is the system's failure, and PLC-35 gives it its own reason and a retry.
            failureReason = gradings.Count > 0
                ? PlacementFailureReason.AiGradingFailed
                : sittingTimedOut
                    ? PlacementFailureReason.InactivityTimeout
                    : PlacementFailureReason.CandidateSkipped;
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

    /// <summary>
    /// #269: which skill a roster position belongs to. The legacy path read
    /// <c>AttemptAnswer.Question.Section.SkillId</c>; a pool answer has no question row, so the same fact
    /// is resolved from the position it was written against (<c>ExamItemId</c>) - one query, reused by
    /// the answered tally and the topic priors so neither can disagree about which skill an answer counts
    /// for.
    /// </summary>
    private async Task<Dictionary<int, int>> LoadSkillByPositionAsync(
        int attemptId, CancellationToken cancellationToken)
        => await RosterSkills.ByAnswerAsync(db, attemptId, cancellationToken);

    /// <summary>
    /// #219 / D5: what the sitting saw per (skill, topic, band). A topic is a property of the pool item
    /// the answer is pinned to, and an answer with no pin has no topic to name G�� it contributes
    /// nothing rather than an invented one.
    /// <para>
    /// <b>Only answered items count.</b> The engine pre-creates an empty row per objective question
    /// when the sitting starts, so a row is not evidence of an answer; the same rule
    /// <see cref="AnswerContent.IsAnswered"/> gives the answered counter and the review page.
    /// </para>
    /// <para>
    /// <b>The signal is coarse on purpose.</b> A topic is <see cref="PlacementTopicPrior.Weak"/> when
    /// fewer than 70% of its items were right G�� the same cutoff the certificate's own accuracy target
    /// uses G�� and <see cref="PlacementTopicPrior.OnLevel"/> otherwise. It is not a proportion and it
    /// is not mastery: it exists so week one can order one topic before another.
    /// </para>
    /// </summary>
    private async Task<Dictionary<int, IReadOnlyList<PlacementTopicPrior>>> LoadTopicPriorsAsync(
        int attemptId, CancellationToken cancellationToken)
    {
        var answers = await db.AttemptAnswers
            .AsNoTracking()
            .Where(a => a.AttemptId == attemptId && a.ItemId != null)
            .Select(a => new
            {
                a.AnswerId,
                a.ItemId,
                a.SelectedOptionId,
                a.SelectedLabel,
                a.ResponseText,
                a.SelectedOptionIdsJson,
                a.IsCorrect,
            })
            .ToListAsync(cancellationToken);

        if (answers.Count == 0)
        {
            return [];
        }

        var itemIds = answers.Select(a => a.ItemId!.Value).Distinct().ToList();
        var items = await db.Items
            .AsNoTracking()
            .Where(i => itemIds.Contains(i.ItemId))
            .Select(i => new { i.ItemId, i.TopicId, Band = i.CefrLevel.Code })
            .ToDictionaryAsync(i => i.ItemId, cancellationToken);

        var skillByAnswer = await LoadSkillByPositionAsync(attemptId, cancellationToken);

        return answers
            .Where(a => AnswerContent.IsAnswered(
                a.SelectedOptionId, a.SelectedLabel, a.ResponseText, a.SelectedOptionIdsJson))
            // #219: only a JUDGED answer says anything about items seen and items correct. A productive
            // task is stored with IsCorrect null on purpose (#253), and counting it as a wrong item
            // would report a writing topic as weak because nothing marked it right - the rubric profile
            // is the productive half's evidence, not this tally.
            .Where(a => a.IsCorrect.HasValue)
            .Where(a => items.ContainsKey(a.ItemId!.Value))
            .Where(a => skillByAnswer.ContainsKey(a.AnswerId))
            .GroupBy(a => skillByAnswer[a.AnswerId])
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PlacementTopicPrior>)g
                    .GroupBy(a => (items[a.ItemId!.Value].TopicId, items[a.ItemId!.Value].Band))
                    .OrderBy(t => t.Key.TopicId)
                    .Select(t =>
                    {
                        var seen = t.Count();
                        var correct = t.Count(a => a.IsCorrect == true);
                        return new PlacementTopicPrior(
                            t.Key.TopicId,
                            t.Key.Band,
                            seen,
                            correct,
                            correct * 100 >= seen * 70
                                ? PlacementTopicPrior.OnLevel
                                : PlacementTopicPrior.Weak);
                    })
                    .ToList());
    }

    /// <summary>
    /// #219 / D5: the productive half's rubric profile, one per skill. Only a graded task contributes:
    /// a pending grading has no scores, and a failed one scored nothing, so neither names a weakest
    /// criterion.
    /// <para>
    /// The priority fix is the grader's own text from the attempt's lowest-scoring graded task G�� the
    /// only row in the system that knows what to do about the weakness it found.
    /// </para>
    /// </summary>
    private async Task<Dictionary<int, PlacementCriterionProfile>> LoadCriterionProfilesAsync(
        int attemptId, CancellationToken cancellationToken)
    {
        // #269: the section of a FREE RESPONSE, resolved from its own row. A pool response has no
        // QuestionId, so `FreeResponse.Question.Section.SectionCriteria` is a navigation through null -
        // inside an EF projection that cannot short-circuit, so the query threw and the criterion profile
        // was written empty. The consequence was silent and wrong: a rubric override (a section marking a
        // criterion out of 3 instead of the table's 6) was ignored, and the weakest criterion was named
        // by the wrong ratio.
        var scoredRaw = await db.AiGradingScores
            .AsNoTracking()
            .Where(s => s.AiGrading.FreeResponse.AttemptId == attemptId
                && s.AiGrading.Status == AiGradingStatus.Done.ToCode())
            .Select(s => new
            {
                s.SkillId,
                s.CriterionId,
                s.GradingCriterion.Code,
                s.Score,
                s.GradingCriterion.MaxScore,
                s.AiGrading.FreeResponseId,
            })
            .ToListAsync(cancellationToken);

        var sectionByResponse = await RosterSkills.ByResponseSectionAsync(db, attemptId, cancellationToken);

        var overrideBySectionAndCriterion = (await db.SectionCriteria
                .AsNoTracking()
                .Where(sc => sectionByResponse.Values.Contains(sc.SectionId))
                .Select(sc => new { sc.SectionId, sc.CriterionId, sc.MaxScore })
                .ToListAsync(cancellationToken))
            .ToDictionary(x => (x.SectionId, x.CriterionId), x => x.MaxScore);

        // UI-20: a section may override the ceiling, so the ratio is read against the score the criterion
        // was actually marked out of rather than its table default.
        var scored = scoredRaw.Select(s =>
        {
            var sectionId = sectionByResponse.GetValueOrDefault(s.FreeResponseId);
            return new
            {
                s.SkillId,
                s.CriterionId,
                s.Code,
                s.Score,
                MaxScore = overrideBySectionAndCriterion.GetValueOrDefault((sectionId, s.CriterionId))
                    ?? s.MaxScore,
            };
        }).ToList();

        if (scored.Count == 0)
        {
            return [];
        }

        var fixes = await db.AiGradings
            .AsNoTracking()
            .Where(g => g.FreeResponse.AttemptId == attemptId
                && g.Status == AiGradingStatus.Done.ToCode()
                && g.PriorityFix != null
                && g.OverallScaled != null)
            .OrderBy(g => g.OverallScaled)
            .ThenBy(g => g.GradingId)
            .Select(g => new { g.SkillId, g.PriorityFix })
            .ToListAsync(cancellationToken);

        var fixBySkill = fixes
            .GroupBy(f => f.SkillId)
            .ToDictionary(g => g.Key, g => g.First().PriorityFix);

        return scored
            .GroupBy(s => s.SkillId)
            .Select(g => new
            {
                SkillId = g.Key,
                Profile = PlacementCriterionProfile.FromScores(
                    g.Select(s => new PlacementCriterionScore(s.CriterionId, s.Code, s.Score, s.MaxScore)),
                    fixBySkill.GetValueOrDefault(g.Key)),
            })
            .Where(x => x.Profile is not null)
            .ToDictionary(x => x.SkillId, x => x.Profile!);
    }

    private static string? BandCodeFor(int? totalScore, IReadOnlyList<ScoreBand> bands) =>
        totalScore is int total ? AttemptEngine.MatchBandCode(total, bands) : null;

    /// <summary>
    /// #259 / PRN-03: <b>a band is not a CEFR level, so a score the cut-score table cannot place has no
    /// level at all.</b>
    /// <para>
    /// This used to call a helper that parsed the attempt's band code as a level. It looked harmless and
    /// was wrong: IELTS, TOEIC and VSTEP spell their band codes <c>A2</c>/<c>B1</c>/…, the very
    /// characters a level uses, so a table with a gap silently produced a level from the band's NAME.
    /// The seeded TOEIC table has five such gaps (56–59, 106–109, 271–274, 396–399, 486–489), and 396–399
    /// sits inside band A2 (225–545) — so a learner scoring 397 was reported at CEFR A2 by a crosswalk
    /// no one wrote and AGENTS.md §9 forbids without an argued case.
    /// </para>
    /// <para>
    /// <b>Reporting the floor instead would be the other fabrication.</b> PreA1 is a claim about the
    /// learner; a missing cut row is a claim about the content. So the skill becomes
    /// <c>not_measured</c> with <see cref="PlacementFailureReason.NoCutScoreRow"/>, which the result
    /// screen already renders as an attention panel with a reason — the same shape a failed grading
    /// gets, and honest for the same reason.
    /// </para>
    /// </summary>
    private void RecordUnplaceable(PlacementSkillResult row)
    {
        row.ResultStatus = PlacementResultStatus.NotMeasured;
        row.IsEstimatedFromOneTask = false;
        row.ReusedFromPrior = false;
        row.CefrLevel = null;
        row.PositionInBand = null;
        row.ContinuousCoordinate = null;
        row.ProjectedBandCode = null;
        row.ProjectedScore = null;
        row.CutScoreTableVersion = null;
        row.MeasuredAt = null;
        row.ExpiresAt = null;
        row.FailureReason = PlacementFailureReason.NoCutScoreRow;
        row.IsTruncated = false;
        // Nothing was read from this skill, so it contributes no hint and no rubric profile.
        row.TopicPriorsJson = null;
        row.CriterionProfileJson = null;
    }

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
