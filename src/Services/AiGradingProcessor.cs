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
        var question = fr.Question;

        try
        {
            // Load applicable criteria: section-specific criteria first, fallback to skill-wide general criteria
            var sectionCriteria = await db.SectionCriteria
                .AsNoTracking()
                .Where(sc => sc.SectionId == question.SectionId)
                .Include(sc => sc.GradingCriterion)
                .OrderBy(sc => sc.GradingCriterion.DisplayOrder)
                .ToListAsync(cancellationToken);

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
            if (question.Section.Modality == Modality.Writing.ToCode())
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
                    await db.SaveChangesAsync(cancellationToken);
                    return false;
                }
            }

            // #152: a spoken answer is graded against the reference its own question declares. The
            // kind is a question-level column, so a scripted question keeps its script even when its
            // group shows a chart. No declared kind means unscripted, not a guess.
            var (referenceText, describesImage) = SpeakingTask.ResolveReference(
                question.SampleAnswerOrImageDesc, question.ReferenceKind);

            var request = new AiGradingRequest(
                fr.FreeResponseId,
                grading.SkillId,
                fr.Attempt.CertId,
                question.Stem,
                question.MinWords,
                question.MaxWords,
                fr.ResponseText ?? string.Empty,
                criteriaDetails,
                grading.AttemptCount,
                fr.AudioPath,
                fr.DurationMs,
                referenceText,
                describesImage,
                question.RequiredKeywords);


            var result = await aiGrader.GradeAsync(request, cancellationToken);

            // E2.1 (#151): a writing rubric must come back whole. The grader refuses to fill a hole
            // itself, and this is the second guard: a grader that answers "success" with fewer marks
            // than were asked for cannot leave a silently averaged row behind. Speaking is exempt on
            // purpose - SpeechSuper drops a criterion its provider did not measure (#152) and says so
            // in the reason.
            if (result.Success && question.Section.Modality == Modality.Writing.ToCode())
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

    /// <summary>E2.1 (#151): a row that is not 'done' holds no marks and no notes.</summary>
    private static void ClearFeedback(AiGrading grading)
    {
        grading.OverallScaled = null;
        grading.BandCode = null;
        grading.PriorityFix = null;
        grading.ModelRewrite = null;
        grading.ErrorSpansJson = null;
    }
}
