using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AttemptEngine
{
    public async Task<AttemptGradingResult> SubmitAttemptAsync(
        int attemptId,
        int userId,
        bool isAutoSubmit,
        CancellationToken cancellationToken = default,
        IReadOnlyList<AttemptFinalDraft>? finalDrafts = null)
    {
        var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var attempt = await db.ExamAttempts
                .Include(a => a.Exam)
                .Include(a => a.Cert)
                .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
                ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

            if (attempt.Status == AttemptStatus.Graded.ToCode()
                || attempt.Status == AttemptStatus.Submitted.ToCode())
            {
                var existingScores = await db.AttemptSkillScores
                    .Where(s => s.AttemptId == attemptId)
                    .ToListAsync(cancellationToken);
                var existingAnswers = await db.AttemptAnswers
                    .Where(a => a.AttemptId == attemptId)
                    .ToListAsync(cancellationToken);

                if (tx is not null) await tx.RollbackAsync(cancellationToken);

                return new AttemptGradingResult(
                    attempt,
                    attempt.TotalScore ?? 0,
                    attempt.BandCode,
                    existingScores,
                    existingAnswers,
                    attempt.SubmittedAt ?? DateTimeOffset.UtcNow,
                    attempt.IsAutoSubmitted);
            }

            if (attempt.Status == AttemptStatus.Abandoned.ToCode())
            {
                throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "This attempt was abandoned.");
            }
            var now = DateTimeOffset.UtcNow;
            if (isAutoSubmit || now >= attempt.ExpiresAt)
            {
                attempt.IsAutoSubmitted = true;
            }

            var freeResponses = attempt.Exam.Purpose == ExamPurpose.Mock.ToCode() || attempt.Exam.Purpose == ExamPurpose.Placement.ToCode()
                ? await db.FreeResponses.Where(fr => fr.AttemptId == attemptId).ToListAsync(cancellationToken)
                : [];
            if (finalDrafts is { Count: > 0 })
            {
                if (finalDrafts.Select(draft => draft.QuestionId).Distinct().Count() != finalDrafts.Count)
                {
                    throw new AttemptEngineException(
                        AttemptEngineException.QuestionNotFound,
                        "A final response draft was submitted more than once.");
                }

                foreach (var finalDraft in finalDrafts)
                {
                    var response = freeResponses.SingleOrDefault(item => item.QuestionId == finalDraft.QuestionId);
                    if (response is null)
                    {
                        var question = await db.Questions
                            .SingleOrDefaultAsync(q =>
                                q.QuestionId == finalDraft.QuestionId
                                && q.ExamId == attempt.ExamId
                                && q.QuestionType == QuestionType.FreeResponse.ToCode(),
                                cancellationToken)
                            ?? throw new AttemptEngineException(
                                AttemptEngineException.QuestionNotFound,
                                $"Free-response question {finalDraft.QuestionId} was not found for this attempt.");

                        response = new FreeResponse
                        {
                            AttemptId = attemptId,
                            QuestionId = question.QuestionId,
                            ExamId = attempt.ExamId,
                            SkillId = question.SkillId,
                            QuestionType = QuestionType.FreeResponse.ToCode(),
                            ResponseText = finalDraft.ResponseText ?? string.Empty,
                            Status = FreeResponseStatus.Draft.ToCode(),
                            DraftSavedAt = now,
                            SubmittedAt = null
                        };
                        freeResponses.Add(response);
                        db.FreeResponses.Add(response);
                    }
                    else
                    {
                        response.ResponseText = finalDraft.ResponseText ?? string.Empty;
                        response.DraftSavedAt = now;
                    }
                }
            }

            foreach (var response in freeResponses.Where(fr => fr.Status == FreeResponseStatus.Draft.ToCode()))
            {
                response.Status = FreeResponseStatus.PendingAi.ToCode();
                response.SubmittedAt = now;
            }

            if (freeResponses.Count > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            var hasPendingMockResponses = freeResponses.Count > 0
                && freeResponses.Any(fr => fr.Status != FreeResponseStatus.Graded.ToCode());
            attempt.SubmittedAt = now;
            attempt.Status = hasPendingMockResponses
                ? AttemptStatus.Submitted.ToCode()
                : AttemptStatus.Graded.ToCode();

            var answers = await db.AttemptAnswers
                .Where(a => a.AttemptId == attemptId)
                .Include(a => a.Question).ThenInclude(q => q.Section).ThenInclude(s => s.Skill)
                .ToListAsync(cancellationToken);
            var questionIds = answers.Select(answer => answer.QuestionId).Distinct().ToList();
            var optionsByQuestion = (await db.QuestionOptions
                .Where(option => questionIds.Contains(option.QuestionId))
                .ToListAsync(cancellationToken))
                .ToLookup(option => option.QuestionId);

            // Score each objective answer
            foreach (var ans in answers.Where(a => a.Question.QuestionType != QuestionType.FreeResponse.ToCode()))
            {
                var opts = optionsByQuestion[ans.QuestionId];
                ans.IsCorrect = EvaluateObjectiveAnswer(
                    ans.Question,
                    opts,
                    ans.SelectedOptionId,
                    ans.SelectedOptionIdsJson,
                    ans.ResponseText);
            }

            // Clean any existing skill scores for idempotent resubmits
            var existingSkillScores = await db.AttemptSkillScores
                .Where(s => s.AttemptId == attemptId)
                .ToListAsync(cancellationToken);
            if (existingSkillScores.Count > 0)
            {
                db.AttemptSkillScores.RemoveRange(existingSkillScores);
            }

            var skillGroups = answers
                .Where(a => a.Question.QuestionType != QuestionType.FreeResponse.ToCode())
                .GroupBy(a => a.Question.Section.Skill)
                .ToList();
            var newSkillScores = new List<AttemptSkillScore>();

            foreach (var group in skillGroups)
            {
                var skill = group.Key;
                var sectionGroups = group.GroupBy(a => a.Question.Section).ToList();
                int rawScore;
                int scaledScore;

                bool hasVaryingWeights = sectionGroups.Any(sg => sg.Key.ScoreWeight != sectionGroups[0].Key.ScoreWeight);
                if (hasVaryingWeights)
                {
                    decimal totalWeight = sectionGroups.Sum(sg => sg.Key.ScoreWeight > 0 ? sg.Key.ScoreWeight : 1.0m);
                    decimal weightedSectionRaw = sectionGroups.Sum(sg =>
                        (decimal)sg.Count(a => a.IsCorrect == true) * (sg.Key.ScoreWeight > 0 ? sg.Key.ScoreWeight : 1.0m)) / totalWeight;

                    rawScore = (int)Math.Round(weightedSectionRaw, MidpointRounding.AwayFromZero);
                    scaledScore = await ComputeScaledScoreAsync(
                        attempt.CertId, skill.SkillId, attempt.ExamId, weightedSectionRaw, group.Count(), cancellationToken);
                }
                else
                {
                    rawScore = group.Count(a => a.IsCorrect == true);
                    scaledScore = await ComputeScaledScoreAsync(
                        attempt.CertId, skill.SkillId, attempt.ExamId, rawScore, group.Count(), cancellationToken);
                }

                newSkillScores.Add(new AttemptSkillScore
                {
                    AttemptId = attempt.AttemptId,
                    SkillId = skill.SkillId,
                    CertId = attempt.CertId,
                    RawScore = rawScore,
                    ScaledScore = scaledScore
                });
            }

            db.AttemptSkillScores.AddRange(newSkillScores);

            var cert = await db.Certificates
                .Where(c => c.CertId == attempt.CertId)
                .Select(c => new { c.Code, c.ScoreAggregationRule })
                .SingleAsync(cancellationToken);
            var totalScore = AggregateTotalScore(cert.ScoreAggregationRule, newSkillScores);
            attempt.TotalScore = totalScore;
            string? matchingBandCode = null;
            if (!hasPendingMockResponses)
            {
                matchingBandCode = await FindBandCodeAsync(attempt.CertId, totalScore, cancellationToken);
                attempt.BandCode = matchingBandCode;
            }
            else
            {
                attempt.BandCode = null;
            }

            // Snapshot matching invariant: PlacementTests.TotalScore must match ExamAttempts.TotalScore
            if (!hasPendingMockResponses)
            {
                var linkedPlacements = await db.PlacementTests
                    .Where(p => p.AttemptId == attempt.AttemptId && p.Status == PlacementStatus.InProgress.ToCode())
                    .ToListAsync(cancellationToken);

                foreach (var placement in linkedPlacements)
                {
                    placement.Status = PlacementStatus.Done.ToCode();
                    placement.TotalScore = totalScore;
                    placement.ResultBandCode = attempt.BandCode;
                    placement.TakenAt = now;
                }
            }

            var hasFreeResponses = freeResponses.Count > 0;
            attempt.Status = hasFreeResponses
                ? AttemptStatus.Submitted.ToCode()
                : AttemptStatus.Graded.ToCode();
            attempt.SubmittedAt = now;
            attempt.TotalScore = totalScore;
            attempt.BandCode = hasPendingMockResponses ? null : matchingBandCode;

            if (freeResponses.Count > 0)
            {
                var responseIds = freeResponses.Select(r => r.FreeResponseId).ToList();
                var existingGradingIds = await db.AiGradings
                    .Where(g => responseIds.Contains(g.FreeResponseId))
                    .Select(g => g.FreeResponseId)
                    .ToListAsync(cancellationToken);

                foreach (var response in freeResponses.Where(r => !existingGradingIds.Contains(r.FreeResponseId)))
                {
                    db.AiGradings.Add(new AiGrading
                    {
                        FreeResponseId = response.FreeResponseId,
                        SkillId = response.SkillId,
                        Status = AiGradingStatus.Pending.ToCode(),
                        AttemptCount = 1,
                        RequestedAt = now
                    });
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            if (tx is not null) await tx.CommitAsync(cancellationToken);

            return new AttemptGradingResult(
                attempt,
                totalScore,
                attempt.BandCode,
                newSkillScores,
                answers,
                now,
                attempt.IsAutoSubmitted);
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
    }

    public async Task FinalizeAiGradedAttemptAsync(int attemptId, CancellationToken cancellationToken = default)
    {
        var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var attempt = await db.ExamAttempts
                .Include(a => a.Exam)
                .SingleOrDefaultAsync(a => a.AttemptId == attemptId, cancellationToken)
                ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

            if (attempt.Status == AttemptStatus.Graded.ToCode()) return;
            if ((attempt.Exam.Purpose != ExamPurpose.Mock.ToCode() && attempt.Exam.Purpose != ExamPurpose.Placement.ToCode())
                || attempt.Status != AttemptStatus.Submitted.ToCode())
            {
                throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Only a submitted mock or placement attempt can be finalized after AI grading.");
            }

            var freeResponses = await db.FreeResponses
                .Where(fr => fr.AttemptId == attemptId)
                .Include(fr => fr.Question).ThenInclude(q => q.Section).ThenInclude(s => s.Skill)
                .Include(fr => fr.AiGradingFreeResponse).ThenInclude(g => g!.AiGradingScores)
                .ToListAsync(cancellationToken);

            if (freeResponses.Count == 0
                || freeResponses.Any(fr => fr.Status != FreeResponseStatus.Graded.ToCode()
                    || fr.AiGradingFreeResponse?.Status != AiGradingStatus.Done.ToCode()))
            {
                throw new AttemptEngineException(AttemptEngineException.AttemptNotFinished, "All free responses must finish AI grading before the result can be finalized.");
            }

            var answers = await db.AttemptAnswers
                .Where(a => a.AttemptId == attemptId)
                .Include(a => a.Question).ThenInclude(q => q.Section).ThenInclude(s => s.Skill)
                .Include(a => a.Question).ThenInclude(q => q.QuestionOptionQuestionNavigations)
                .ToListAsync(cancellationToken);

            attempt.TotalScore = null;
            await db.SaveChangesAsync(cancellationToken);

            var existingScores = await db.AttemptSkillScores
                .Where(s => s.AttemptId == attemptId)
                .ToListAsync(cancellationToken);
            db.AttemptSkillScores.RemoveRange(existingScores);

            foreach (var response in freeResponses)
            {
                var eval = EvaluateWritingResponse(response.Question, response.ResponseText);
                if (eval.IsUnderLength && response.AiGradingFreeResponse != null)
                {
                    foreach (var s in response.AiGradingFreeResponse.AiGradingScores)
                    {
                        if (string.IsNullOrWhiteSpace(s.Comment))
                        {
                            s.Comment = "IsUnderLength=true; Length penalty applied.";
                        }
                        else if (!s.Comment.Contains("IsUnderLength"))
                        {
                            s.Comment = $"{s.Comment} [IsUnderLength=true]";
                        }
                    }

                    var hasExistingRec = await db.Recommendations
                        .AnyAsync(r => r.GradingId == response.AiGradingFreeResponse.GradingId && r.Text.Contains("IsUnderLength"), cancellationToken);
                    if (!hasExistingRec)
                    {
                        db.Recommendations.Add(new Recommendation
                        {
                            UserId = attempt.UserId,
                            CertId = attempt.CertId,
                            AttemptId = attempt.AttemptId,
                            GradingId = response.AiGradingFreeResponse.GradingId,
                            SkillId = response.SkillId,
                            Severity = "minor",
                            Text = $"IsUnderLength=true: Bài viết có {eval.WordCount} từ, dưới mức tối thiểu {response.Question.MinWords} từ.",
                            CreatedAt = DateTimeOffset.UtcNow
                        });
                    }
                }
            }

            var responsePointsBySkill = new Dictionary<int, decimal>();
            foreach (var response in freeResponses)
            {
                var sectionCriteria = await db.SectionCriteria
                    .Where(sc => sc.SectionId == response.Question.SectionId)
                    .Include(sc => sc.GradingCriterion)
                    .ToListAsync(cancellationToken);

                var maxScore = sectionCriteria.Count > 0
                    ? sectionCriteria.Sum(sc => sc.MaxScore ?? sc.GradingCriterion.MaxScore)
                    : await db.GradingCriteria
                        .Where(c => c.SkillId == response.SkillId)
                        .SumAsync(c => (decimal?)c.MaxScore, cancellationToken) ?? 0m;

                var fraction = maxScore > 0m
                    ? Math.Clamp((response.AiGradingFreeResponse!.OverallScaled ?? 0m) / maxScore, 0m, 1m)
                    : 0m;
                responsePointsBySkill[response.SkillId] = responsePointsBySkill.GetValueOrDefault(response.SkillId) + fraction;
            }

            foreach (var answer in answers.Where(a => a.Question.QuestionType != QuestionType.FreeResponse.ToCode()))
            {
                var opts = answer.Question.QuestionOptionQuestionNavigations;
                answer.IsCorrect = EvaluateObjectiveAnswer(
                    answer.Question,
                    opts,
                    answer.SelectedOptionId,
                    answer.SelectedOptionIdsJson,
                    answer.ResponseText);
            }

            var skills = answers.Select(a => a.Question.Section.Skill)
                .Concat(freeResponses.Select(fr => fr.Question.Section.Skill))
                .DistinctBy(s => s.SkillId)
                .ToList();

            var newSkillScores = new List<AttemptSkillScore>();
            foreach (var skill in skills)
            {
                var objectiveAnswers = answers.Where(a => a.Question.Section.SkillId == skill.SkillId && a.Question.QuestionType != QuestionType.FreeResponse.ToCode()).ToList();
                var freeResponsesInSkill = freeResponses.Where(fr => fr.SkillId == skill.SkillId).ToList();
                var totalQuestionsInSkill = objectiveAnswers.Count + freeResponsesInSkill.Count;
                if (totalQuestionsInSkill == 0) continue;

                var hasScales = await db.ScoreScales.AnyAsync(s => s.CertId == attempt.CertId && s.SkillId == skill.SkillId, cancellationToken);
                if (objectiveAnswers.Count == 0 && !hasScales && freeResponsesInSkill.Count == 0) continue;

                int scaledScore;
                decimal rawScore;

                if (freeResponsesInSkill.Count > 0 && objectiveAnswers.Count == 0)
                {
                    // Pure free response skill (e.g. IELTS Writing Task 1 + Task 2)
                    var frSections = freeResponsesInSkill.Select(fr => fr.Question.Section).DistinctBy(s => s.SectionId).ToList();
                    decimal totalWeight = frSections.Sum(s => s.ScoreWeight > 0 ? s.ScoreWeight : 1.0m);
                    decimal weightedSectionSum = 0m;

                    foreach (var fr in freeResponsesInSkill)
                    {
                        var secWeight = fr.Question.Section.ScoreWeight > 0 ? fr.Question.Section.ScoreWeight : 1.0m;
                        var frScore = fr.AiGradingFreeResponse?.OverallScaled ?? 0m;
                        weightedSectionSum += frScore * secWeight;
                    }

                    decimal weightedScore = totalWeight > 0 ? weightedSectionSum / totalWeight : 0m;
                    rawScore = weightedScore;

                    var certRule = await db.Certificates
                        .Where(c => c.CertId == attempt.CertId)
                        .Select(c => c.ScoreAggregationRule)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (string.Equals(certRule, "avg_half_step", StringComparison.OrdinalIgnoreCase))
                    {
                        scaledScore = RoundHalfStep((double)weightedScore);
                    }
                    else
                    {
                        scaledScore = (int)Math.Round(weightedScore, MidpointRounding.AwayFromZero);
                    }
                }
                else
                {
                    rawScore = objectiveAnswers.Count(a => a.IsCorrect == true) + responsePointsBySkill.GetValueOrDefault(skill.SkillId);
                    scaledScore = await ComputeScaledScoreAsync(
                        attempt.CertId,
                        skill.SkillId,
                        attempt.ExamId,
                        rawScore,
                        totalQuestionsInSkill,
                        cancellationToken);
                }

                newSkillScores.Add(new AttemptSkillScore
                {
                    AttemptId = attempt.AttemptId,
                    SkillId = skill.SkillId,
                    CertId = attempt.CertId,
                    RawScore = (int)Math.Round(rawScore, MidpointRounding.AwayFromZero),
                    ScaledScore = scaledScore
                });
            }

            db.AttemptSkillScores.AddRange(newSkillScores);
            await db.SaveChangesAsync(cancellationToken);

            var cert = await db.Certificates
                .Where(c => c.CertId == attempt.CertId)
                .Select(c => new { c.Code, c.ScoreAggregationRule })
                .SingleAsync(cancellationToken);
            var totalScore = AggregateTotalScore(cert.ScoreAggregationRule, newSkillScores);
            attempt.TotalScore = totalScore;
            attempt.BandCode = await FindBandCodeAsync(attempt.CertId, totalScore, cancellationToken);
            attempt.Status = AttemptStatus.Graded.ToCode();
            await db.SaveChangesAsync(cancellationToken);
            if (tx is not null) await tx.CommitAsync(cancellationToken);
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
        }
    }
}
