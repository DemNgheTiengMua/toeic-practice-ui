using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Services;

public sealed class AiGradingProcessor(
    CompanionDbContext db,
    IAiGrader aiGrader,
    ILogger<AiGradingProcessor>? logger = null,
    IAttemptEngine? attemptEngine = null) : IAiGradingProcessor
{
    private readonly IAttemptEngine _attemptEngine = attemptEngine ?? new AttemptEngine(db);

    public async Task<int> ProcessPendingGradingsAsync(int batchSize = 10, CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0) batchSize = 10;

        var pendingGradings = await db.AiGradings
            .Include(g => g.FreeResponse)
                .ThenInclude(fr => fr.Question)
                    .ThenInclude(q => q.Section)
            .Include(g => g.FreeResponse)
                .ThenInclude(fr => fr.Attempt)
                    .ThenInclude(a => a.Exam)
            .Where(g => g.Status == AiGradingStatus.Pending.ToCode())
            .OrderBy(g => g.RequestedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (pendingGradings.Count == 0) return 0;

        int processed = 0;
        foreach (var grading in pendingGradings)
        {
            await ProcessSingleGradingAsync(grading, cancellationToken);
            processed++;
        }

        return processed;
    }

    public async Task<bool> GradeFreeResponseAsync(int freeResponseId, CancellationToken cancellationToken = default)
    {
        var grading = await db.AiGradings
            .Include(g => g.FreeResponse)
                .ThenInclude(fr => fr.Question)
                    .ThenInclude(q => q.Section)
            .Include(g => g.FreeResponse)
                .ThenInclude(fr => fr.Attempt)
                    .ThenInclude(a => a.Exam)
            .Where(g => g.FreeResponseId == freeResponseId && g.Status == AiGradingStatus.Pending.ToCode())
            .SingleOrDefaultAsync(cancellationToken);

        if (grading is null) return false;

        return await ProcessSingleGradingAsync(grading, cancellationToken);
    }

    private async Task<bool> ProcessSingleGradingAsync(AiGrading grading, CancellationToken cancellationToken)
    {
        var fr = grading.FreeResponse;

        // #269: a pool free response has no Question row - it names a roster position instead. Reading
        // `fr.Question.SectionId` threw NullReferenceException inside the EF parameter evaluation, so
        // every pool writing/speaking submission failed grading and the learner was charged for a
        // result that never arrived. Everything this method needs from the question is resolved inside
        // the guard below, from whichever store the paper uses.
        try
        {
            var position = await LoadGradedPositionAsync(fr, cancellationToken);
            var question = fr.Question;
            var frSectionId = position?.SectionId ?? question?.SectionId;
            var sectionModality = position?.Modality ?? question?.Section?.Modality;
            var isWriting = sectionModality == Modality.Writing.ToCode();

            // Load applicable criteria: section-specific criteria first, fallback to skill-wide general criteria
            var sectionCriteria = frSectionId is int sectionId
                ? await db.SectionCriteria
                    .AsNoTracking()
                    .Where(sc => sc.SectionId == sectionId)
                    .Include(sc => sc.GradingCriterion)
                    .OrderBy(sc => sc.GradingCriterion.DisplayOrder)
                    .ToListAsync(cancellationToken)
                : [];

            // #192: the seven-band grid travels with each criterion, read in one query for the whole
            // rubric. A criterion with no grid keeps its prose, so every rubric authored before this
            // ticket grades exactly as it did.
            List<GradingCriterionDetail> criteriaDetails;
            if (sectionCriteria.Count > 0)
            {
                criteriaDetails = sectionCriteria.Select(sc => new GradingCriterionDetail(
                    sc.CriterionId,
                    sc.GradingCriterion.Code,
                    sc.GradingCriterion.Name,
                    sc.MaxScore ?? sc.GradingCriterion.MaxScore,
                    sc.GradingCriterion.Weight,
                    sc.GradingCriterion.RubricDescription)).ToList();
            }
            else
            {
                var generalCriteria = await db.GradingCriteria
                    .AsNoTracking()
                    .Where(c => c.SkillId == grading.SkillId)
                    .OrderBy(c => c.DisplayOrder)
                    .ToListAsync(cancellationToken);

                criteriaDetails = generalCriteria.Select(c => new GradingCriterionDetail(
                    c.CriterionId,
                    c.Code,
                    c.Name,
                    c.MaxScore,
                    c.Weight,
                    c.RubricDescription)).ToList();
            }

            // The grid is per criterion. One grouped read, not a query per criterion: a rubric is at
            // most four of them, but the loop version reads as N+1 and becomes N+1 the moment a
            // section grows.
            var appliedIds = criteriaDetails.Select(c => c.CriterionId).ToArray();
            var gridByCriterion = appliedIds.Length == 0
                ? new Dictionary<int, IReadOnlyList<GradingCriterionBandDescriptor>>()
                : await db.GradingCriterionDescriptors
                    .AsNoTracking()
                    .Where(d => appliedIds.Contains(d.CriterionId))
                    .GroupBy(d => d.CriterionId)
                    .Select(g => new
                    {
                        CriterionId = g.Key,
                        Bands = g.OrderBy(d => d.Band)
                                 .Select(d => new GradingCriterionBandDescriptor(d.Band, d.Descriptor, d.BinaryGates))
                                 .ToList(),
                    })
                    .ToDictionaryAsync(
                        x => x.CriterionId,
                        x => (IReadOnlyList<GradingCriterionBandDescriptor>)x.Bands,
                        cancellationToken);

            criteriaDetails = criteriaDetails
                .Select(c => c with { Descriptors = gridByCriterion.GetValueOrDefault(c.CriterionId) })
                .ToList();

            // #193: the three calibrated anchors, read in one grouped query for the same reason as the
            // grid - a rubric is at most four criteria, and the loop version would become N+1.
            var anchorByCriterion = appliedIds.Length == 0
                ? new Dictionary<int, IReadOnlyList<GradingCriterionBandAnchor>>()
                : await db.GradingCriterionAnchors
                    .AsNoTracking()
                    .Where(a => appliedIds.Contains(a.CriterionId))
                    .GroupBy(a => a.CriterionId)
                    .Select(g => new
                    {
                        CriterionId = g.Key,
                        Anchors = g.OrderBy(a => a.Band)
                                   .Select(a => new GradingCriterionBandAnchor(a.Band, a.ResponseText))
                                   .ToList(),
                    })
                    .ToDictionaryAsync(
                        x => x.CriterionId,
                        x => (IReadOnlyList<GradingCriterionBandAnchor>)x.Anchors,
                        cancellationToken);

            criteriaDetails = criteriaDetails
                .Select(c => c with { Anchors = anchorByCriterion.GetValueOrDefault(c.CriterionId) })
                .ToList();

            // #193: a locked rubric was calibrated against all three anchors, so grading it without one
            // moves the standard under the mark. The lock is what makes this a refusal rather than an
            // authoring wish: an unlocked rubric is still being written, and every rubric that predates
            // the anchors carries none, so only a locked one is refused. Unweighted here on purpose -
            // the guard is the lock, and a skill modality check would be a second rule to keep in step.
            var rubricLocked = await db.GradingRubricLocks
                .AsNoTracking()
                .AnyAsync(l => l.SkillId == grading.SkillId, cancellationToken);

            if (rubricLocked)
            {
                var unanchored = criteriaDetails.Where(c => !c.IsAnchored).ToList();
                if (unanchored.Count > 0)
                {
                    // The band is named as well as the criterion: "W_TR has no anchors" and "W_TR has no
                    // band-3 anchor" are different rows for the author to write.
                    var named = string.Join(", ", unanchored.Select(c =>
                        $"{c.Code} ({c.CriterionId})"
                        + (c.MissingAnchorBands.Count > 0
                            ? $" is missing its anchor for band {string.Join(", band ", c.MissingAnchorBands)}"
                            : " has no anchor response")));
                    logger?.LogWarning(
                        "GradingId {GradingId} for FreeResponseId {FreeResponseId} failed: {Criteria}",
                        grading.GradingId, fr.FreeResponseId, named);

                    grading.Status = AiGradingStatus.Failed.ToCode();
                    grading.FailureReason = $"Nothing was scored: {named}. A locked rubric is graded against three benchmark anchors at bands 2, 3 and 4; author the missing ones, then grade again.";
                    if (grading.FailureReason.Length > 400) grading.FailureReason = grading.FailureReason[..399] + "…";
                    grading.CompletedAt = DateTimeOffset.UtcNow;
                    ClearFeedback(grading);
                    // #217 / PLC-35: settle on EVERY failure path, not only the grader's. Leaving the
                    // response `pending_ai` keeps the attempt `submitted` forever, so the learner waits
                    // for a result that never arrives and the sitting cannot record the honest
                    // `not_measured`. This branch is a configuration fault an administrator can leave in
                    // place for days, which makes it the one most likely to strand a learner.
                    await SettleFreeResponseAsync(fr.FreeResponseId, cancellationToken);
                    await db.SaveChangesAsync(cancellationToken);
                    return false;
                }
            }

            // #192: a written answer is scored against a written standard. A criterion with no
            // descriptor text cannot be scored against anything, and a rubric that silently scores
            // three of four criteria reports a composite that is not the one its authors wrote
            // (D0.6: M_t = 1/4 * sum c_j). So the whole grading is Failed, and the reason names the
            // codes - never a filled-in score, never an inferred descriptor.
            //
            // Speaking is exempt on purpose: SpeechSuper measures pronunciation and fluency itself
            // (#152) and drops a criterion its provider did not measure with its own stated reason.
            // Its completeness is enforced at the lock door instead, where every skill modality is
            // required to carry seven bands before it can be published against.
            if (isWriting)
            {
                var ungradable = criteriaDetails.Where(c => !c.IsGradeable).ToList();
                if (ungradable.Count > 0)
                {
                    var named = string.Join(", ", ungradable.Select(c =>
                        $"{c.Code} ({c.CriterionId})"
                        + (c.MissingBands.Count > 0
                            ? $" is missing band(s) {string.Join(",", c.MissingBands)}"
                            : " has no descriptor text")));
                    logger?.LogWarning(
                        "GradingId {GradingId} for FreeResponseId {FreeResponseId} failed: {Criteria}",
                        grading.GradingId, fr.FreeResponseId, named);

                    grading.Status = AiGradingStatus.Failed.ToCode();
                    grading.FailureReason = $"Nothing was scored: {named}. An administrator has to write a descriptor for every band, then grade again.";
                    if (grading.FailureReason.Length > 400) grading.FailureReason = grading.FailureReason[..399] + "…";
                    grading.CompletedAt = DateTimeOffset.UtcNow;
                    ClearFeedback(grading);
                    // #217 / PLC-35: settle here too. A rubric with a band missing its descriptor is a
                    // configuration fault, not a transient one, so a learner can sit in `pending_ai` for
                    // as long as nobody edits the rubric.
                    await SettleFreeResponseAsync(fr.FreeResponseId, cancellationToken);
                    await db.SaveChangesAsync(cancellationToken);
                    return false;
                }
            }

            // #152: a spoken answer is graded against the reference its own question declares. The
            // kind is a question-level column, so a scripted question keeps its script even when its
            // group shows a chart. No declared kind means unscripted, not a guess.
            // #269: a pool task carries the same facts on its pinned version, so the position wins
            // where it exists and the legacy row is the fallback - never a null stem.
            var (referenceText, describesImage) = SpeakingTask.ResolveReference(
                position?.ReferenceText ?? question?.SampleAnswerOrImageDesc,
                position?.ReferenceKind ?? question?.ReferenceKind);

            var request = new AiGradingRequest(
                fr.FreeResponseId,
                grading.SkillId,
                fr.Attempt.CertId,
                position?.Stem ?? question?.Stem ?? string.Empty,
                position?.MinWords ?? question?.MinWords,
                position?.MaxWords ?? question?.MaxWords,
                fr.ResponseText ?? string.Empty,
                criteriaDetails,
                grading.AttemptCount,
                fr.AudioPath,
                fr.DurationMs,
                referenceText,
                describesImage,
                position?.RequiredKeywords ?? question?.RequiredKeywords);


            var result = await aiGrader.GradeAsync(request, cancellationToken);

            // E2.1 (#151): a writing rubric must come back whole. The grader refuses to fill a hole
            // itself, and this is the second guard: a grader that answers "success" with fewer marks
            // than were asked for cannot leave a silently averaged row behind. Speaking is exempt on
            // purpose - SpeechSuper drops a criterion its provider did not measure (#152) and says so
            // in the reason.
            if (result.Success && isWriting)
            {
                var missing = criteriaDetails
                    .Where(c => c.IsGradeable && result.Scores.All(s => s.CriterionId != c.CriterionId))
                    .ToList();
                if (missing.Count > 0)
                {
                    result = new AiGradingResult(
                        Success: false,
                        FailureReason: $"The grader returned no score for {string.Join(", ", missing.Select(c => $"{c.Code} ({c.CriterionId})"))}. "
                            + "Nothing was scored; grade again.",
                        Model: result.Model,
                        Scores: Array.Empty<AiCriterionScoreResult>());
                }
            }

            if (result.Success)
            {
                if (!string.IsNullOrEmpty(result.Transcript) && string.IsNullOrWhiteSpace(fr.ResponseText))
                {
                    fr.ResponseText = result.Transcript;
                }

                // Clear any existing scores for this grading (e.g. from retried attempts)
                var existingScores = await db.AiGradingScores
                    .Where(s => s.GradingId == grading.GradingId)
                    .ToListAsync(cancellationToken);
                db.AiGradingScores.RemoveRange(existingScores);

                decimal totalScore = 0m;
                decimal rubricMax = 0m;
                var marks = new List<AttemptEngine.RubricMark>(result.Scores.Count);
                foreach (var s in result.Scores)
                {
                    var detail = criteriaDetails.FirstOrDefault(c => c.CriterionId == s.CriterionId);
                    var max = detail?.MaxScore ?? s.Score;
                    var clampedScore = Math.Clamp(s.Score, 0m, max);

                    db.AiGradingScores.Add(new AiGradingScore
                    {
                        GradingId = grading.GradingId,
                        CriterionId = s.CriterionId,
                        SkillId = grading.SkillId,
                        Score = clampedScore,
                        Comment = s.Comment
                    });
                    marks.Add(new AttemptEngine.RubricMark(clampedScore, max, detail?.Weight ?? 1m));
                }

                // E2.1 (#151): Weight is what decides how much a criterion is worth. Until now it was
                // sent to the model and then ignored, so a rubric that called grammar twice as
                // important as spelling produced the same total either way.
                (totalScore, rubricMax) = AttemptEngine.WeightedRubricTotal(marks);

                fr.Status = FreeResponseStatus.Graded.ToCode();
                grading.Status = AiGradingStatus.Done.ToCode();
                grading.CompletedAt = DateTimeOffset.UtcNow;
                grading.Model = result.Model ?? FakeAiGrader.DefaultModel;
                grading.FailureReason = null;
                grading.OverallScaled = totalScore;
                // E2.1 (#151): the feedback the learner acts on, stored beside the marks it explains.
                grading.PriorityFix = result.PriorityFix;
                grading.ModelRewrite = result.ModelRewrite;
                grading.ErrorSpansJson = AiErrorSpan.Serialize(result.ErrorSpans ?? []);

                await db.SaveChangesAsync(cancellationToken);

                // Check if all free responses for this attempt are now graded
                var anyPending = await db.FreeResponses
                    .AnyAsync(r => r.AttemptId == fr.AttemptId && r.Status != FreeResponseStatus.Graded.ToCode(), cancellationToken);

                if (!anyPending)
                {
                    var purpose = fr.Attempt.Exam.Purpose;
                    if (purpose == ExamPurpose.Mock.ToCode() || purpose == ExamPurpose.Placement.ToCode())
                    {
                        // #191: `FinalizeAiGradedAttemptAsync` owns the whole result, including the
                        // placement snapshot. This used to patch the snapshot from here as well, which
                        // made the processor a second writer of the row the confirmed placement defect
                        // is about.
                        await _attemptEngine.FinalizeAiGradedAttemptAsync(fr.AttemptId, cancellationToken);
                    }
                    else
                    {
                        var attempt = await db.ExamAttempts.FindAsync(new object[] { fr.AttemptId }, cancellationToken);
                        if (attempt != null && attempt.Status != AttemptStatus.Graded.ToCode())
                        {
                            attempt.Status = AttemptStatus.Graded.ToCode();
                            if (purpose == ExamPurpose.SpeakingPractice.ToCode()
                                || purpose == ExamPurpose.WritingPractice.ToCode())
                            {
                                // A skill practice scores one rubric; the attempt itself claims no
                                // certificate total. Its band is the estimate over every graded
                                // response, stamped on the attempt's gradings so the column and the
                                // review page carry one number. Zero earns none.
                                attempt.TotalScore = null;
                                var band = await AttemptEngine.EstimateBandForAttemptAsync(
                                    db, attempt.AttemptId, attempt.CertId, cancellationToken);
                                var gradings = await db.AiGradings
                                    .Where(g => g.FreeResponse.AttemptId == attempt.AttemptId)
                                    .ToListAsync(cancellationToken);
                                foreach (var g in gradings) g.BandCode = band?.Code;
                            }

                            await db.SaveChangesAsync(cancellationToken);
                        }
                    }
                }

                return true;
            }
            else
            {
                grading.Status = AiGradingStatus.Failed.ToCode();
                grading.FailureReason = result.FailureReason ?? "Grading failed.";
                grading.CompletedAt = DateTimeOffset.UtcNow;
                // E2.1 (#151): a failed grading carries no marks and no notes. Clearing them here is what
                // CK_AiGradings_FeedbackOnlyWhenDone enforces: the row must not read as "scored badly".
                ClearFeedback(grading);
                // #217 / PLC-35: the task is SETTLED, even though it scored nothing. FreeResponses has
                // no `failed` state - its vocabulary is draft / pending_ai / graded - and leaving it
                // pending_ai would keep the attempt `submitted` forever, so the sitting could never
                // record the honest `not_measured` PLC-35 asks for and the learner would wait for a
                // result that never arrives. The distinction that matters is carried on the AiGrading
                // row (`failed` + its reason), which is what the placement writer and the retry read.
                await SettleFreeResponseAsync(fr.FreeResponseId, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                return false;
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogError(ex, "Unexpected error while grading FreeResponseId {FreeResponseId} (GradingId {GradingId})", fr.FreeResponseId, grading.GradingId);
            grading.Status = AiGradingStatus.Failed.ToCode();
            grading.FailureReason = (ex.InnerException?.Message ?? ex.Message);
            grading.CompletedAt = DateTimeOffset.UtcNow;
            ClearFeedback(grading);
            // #217 / PLC-35: an unexpected fault settles the task as well. This is the last branch the
            // learner can be stranded in, and it is the one nobody can predict: whatever threw, the
            // answer is not going to be graded on this attempt, so the sitting has to be allowed to
            // finish with an honest `not_measured` rather than stay `submitted` forever.
            await SettleFreeResponseAsync(fr.FreeResponseId, cancellationToken);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception saveEx)
            {
                logger?.LogError(saveEx, "Failed to persist failure status for GradingId {GradingId}", grading.GradingId);
            }
            return false;
        }
    }

    /// <summary>
    /// #217 / PLC-35: a task whose grading failed is settled, not still queued. The row's own
    /// vocabulary has no `failed`; the AiGrading row carries that fact and its reason.
    /// </summary>
    private async Task SettleFreeResponseAsync(int freeResponseId, CancellationToken cancellationToken)
    {
        var response = await db.FreeResponses.SingleOrDefaultAsync(fr => fr.FreeResponseId == freeResponseId, cancellationToken);
        if (response is not null && response.Status == FreeResponseStatus.PendingAi.ToCode())
        {
            response.Status = FreeResponseStatus.Graded.ToCode();
        }
    }

    /// <summary>E2.1 (#151): a row that is not 'done' holds no marks and no notes.</summary>
    private static void ClearFeedback(AiGrading grading)
    {
        grading.OverallScaled = null;
        grading.BandCode = null;
        grading.PriorityFix = null;
        grading.ModelRewrite = null;
        grading.ErrorSpansJson = null;
    }

    /// <summary>
    /// #269: the paper position a graded response names, with the four facts grading reads from it -
    /// stem, word window, required keywords and modality. A pool task has no Question row, so the pinned
    /// version is the only place its content lives; a legacy response returns null and the caller keeps
    /// reading the question row it was answered from.
    /// </summary>
    private async Task<GradedPosition?> LoadGradedPositionAsync(
        BusinessObjects.Entities.FreeResponse fr, CancellationToken cancellationToken)
    {
        if (fr.ExamItemId is not int examItemId)
        {
            return null;
        }

        var position = await db.ExamItems
            .AsNoTracking()
            .Where(i => i.ExamItemId == examItemId)
            .Join(db.ItemVersions.AsNoTracking(),
                i => i.ItemVersionId, v => v.ItemVersionId,
                (i, v) => new { i.SectionId, v.Stem, v.MinWords, v.MaxWords, v.RequiredKeywords, v.ReferenceText, v.ReferenceKind })
            .FirstOrDefaultAsync(cancellationToken);

        if (position is null)
        {
            return null;
        }

        var modality = await db.Sections
            .AsNoTracking()
            .Where(s => s.SectionId == position.SectionId)
            .Select(s => s.Modality)
            .FirstOrDefaultAsync(cancellationToken);

        return new GradedPosition(
            position.SectionId, modality, position.Stem, position.MinWords, position.MaxWords,
            position.RequiredKeywords, position.ReferenceText, position.ReferenceKind);
    }

    private sealed record GradedPosition(
        int SectionId,
        string? Modality,
        string Stem,
        int? MinWords,
        int? MaxWords,
        string? RequiredKeywords,
        string? ReferenceText,
        string? ReferenceKind);
}
