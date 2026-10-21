using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Services;

/// <summary>
/// Dedicated processor for asynchronous AI grading of free responses.
/// Drains pending queue, resolves rubric criteria, clamps scores, dispatches to <see cref="IAiGrader"/>,
/// populates <see cref="AiGradingScore"/> rows, and finalizes <see cref="ExamAttempt"/> records.
/// </summary>
public interface IAiGradingProcessor
{
    /// <summary>
    /// Processes up to <paramref name="batchSize"/> pending AI gradings in the queue.
    /// Handles both written (text) and spoken (audio) free responses.
    /// </summary>
    Task<int> ProcessPendingGradingsAsync(int batchSize = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Grades a single pending AI grading associated with the specified free response ID.
    /// </summary>
    Task<bool> GradeFreeResponseAsync(int freeResponseId, CancellationToken cancellationToken = default);
}

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

            List<GradingCriterionDetail> criteriaDetails;
            if (sectionCriteria.Count > 0)
            {
                criteriaDetails = sectionCriteria.Select(sc => new GradingCriterionDetail(
                    sc.CriterionId,
                    sc.GradingCriterion.Code,
                    sc.GradingCriterion.Name,
                    sc.MaxScore ?? sc.GradingCriterion.MaxScore,
                    sc.GradingCriterion.Weight)).ToList();
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
                    c.Weight)).ToList();
            }

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
                fr.DurationMs);

            var result = await aiGrader.GradeAsync(request, cancellationToken);

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
                    totalScore += clampedScore;
                }

                fr.Status = FreeResponseStatus.Graded.ToCode();
                grading.Status = AiGradingStatus.Done.ToCode();
                grading.CompletedAt = DateTimeOffset.UtcNow;
                grading.Model = result.Model ?? FakeAiGrader.DefaultModel;
                grading.FailureReason = null;
                grading.OverallScaled = totalScore;

                await db.SaveChangesAsync(cancellationToken);

                // Check if all free responses for this attempt are now graded
                var anyPending = await db.FreeResponses
                    .AnyAsync(r => r.AttemptId == fr.AttemptId && r.Status != FreeResponseStatus.Graded.ToCode(), cancellationToken);

                if (!anyPending)
                {
                    var purpose = fr.Attempt.Exam.Purpose;
                    if (purpose == ExamPurpose.Mock.ToCode() || purpose == ExamPurpose.Placement.ToCode())
                    {
                        await _attemptEngine.FinalizeAiGradedAttemptAsync(fr.AttemptId, cancellationToken);
                        if (purpose == ExamPurpose.Placement.ToCode())
                        {
                            var placement = await db.PlacementTests.FirstOrDefaultAsync(p => p.AttemptId == fr.AttemptId, cancellationToken);
                            if (placement != null)
                            {
                                var finalizedAttempt = await db.ExamAttempts.FindAsync(new object[] { fr.AttemptId }, cancellationToken);
                                if (finalizedAttempt != null)
                                {
                                    placement.TotalScore = finalizedAttempt.TotalScore;
                                    placement.ResultBandCode = finalizedAttempt.BandCode;
                                    await db.SaveChangesAsync(cancellationToken);
                                }
                            }
                        }
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
                                attempt.TotalScore = null;
                                attempt.BandCode = null;
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
}
