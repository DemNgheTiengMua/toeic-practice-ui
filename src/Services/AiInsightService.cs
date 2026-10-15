using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Services;

public class AiInsightException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public interface IAiInsightService
{
    Task<AiInsightView> GetOrCreatePracticeInsightAsync(int userId, int sessionId, CancellationToken cancellationToken = default);
    Task<AiInsightView> GetOrCreateExamInsightAsync(int userId, int attemptId, CancellationToken cancellationToken = default);
    Task RetryPracticeInsightAsync(int userId, int sessionId, CancellationToken cancellationToken = default);
    Task RetryExamInsightAsync(int userId, int attemptId, CancellationToken cancellationToken = default);
    Task RetryInsightAsync(int userId, int insightId, CancellationToken cancellationToken = default);
    Task<int> ProcessPendingInsightsAsync(CancellationToken cancellationToken = default);
}

public sealed class AiInsightService(
    CompanionDbContext db,
    IAiInsightGenerator generator,
    ILogger<AiInsightService> logger) : IAiInsightService
{
    private static readonly ConcurrentDictionary<int, int> RetryCounts = new();

    public async Task<AiInsightView> GetOrCreatePracticeInsightAsync(int userId, int sessionId, CancellationToken cancellationToken = default)
    {
        var session = await db.PracticeSessions
            .Include(s => s.Section)
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.UserId == userId, cancellationToken);

        if (session is null)
        {
            throw new AiInsightException(ApiError.Codes.PracticeSessionNotFound, $"Practice session {sessionId} was not found.");
        }

        // If session is still in progress, finish it or ensure answers are final
        if (session.Status == PracticeStatus.InProgress.ToCode())
        {
            session.Status = PracticeStatus.Finished.ToCode();
            session.FinishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        // Query answers to compute source fingerprint
        var answers = await db.PracticeAnswers
            .AsNoTracking()
            .Where(pa => pa.SessionId == sessionId)
            .OrderBy(pa => pa.DisplayOrder)
            .ToListAsync(cancellationToken);

        var fingerprint = ComputePracticeFingerprint(userId, sessionId, session.Section.CertId, session.SectionId, answers);

        // Check if an insight already exists for this practice session
        var existing = await db.AiInsights
            .FirstOrDefaultAsync(i => i.UserId == userId
                && i.Scope == AiInsightScope.PracticeSession.ToCode()
                && i.PracticeSessionId == sessionId, cancellationToken);

        if (existing is not null)
        {
            var recos = await LoadRecommendationsAsync(existing.InsightId, userId, cancellationToken);
            return ToInsightView(existing, recos);
        }

        // Check if an insight with the same fingerprint exists
        var existingFingerprint = await db.AiInsights
            .FirstOrDefaultAsync(i => i.UserId == userId
                && i.Scope == AiInsightScope.PracticeSession.ToCode()
                && i.SourceFingerprint == fingerprint, cancellationToken);

        if (existingFingerprint is not null)
        {
            var recos = await LoadRecommendationsAsync(existingFingerprint.InsightId, userId, cancellationToken);
            return ToInsightView(existingFingerprint, recos);
        }

        // Create new pending insight
        var newInsight = new AiInsight
        {
            UserId = userId,
            CertId = session.Section.CertId,
            Scope = AiInsightScope.PracticeSession.ToCode(),
            PracticeSessionId = sessionId,
            AttemptId = null,
            LearnerPathId = null,
            Status = AiInsightStatus.Pending.ToCode(),
            SourceFingerprint = fingerprint,
            CreatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            db.AiInsights.Add(newInsight);
            await db.SaveChangesAsync(cancellationToken);
            return ToInsightView(newInsight, Array.Empty<RecommendationItem>());
        }
        catch (DbUpdateException)
        {
            db.Entry(newInsight).State = EntityState.Detached;
            var concurrentExisting = await db.AiInsights
                .FirstOrDefaultAsync(i => i.UserId == userId
                    && i.Scope == AiInsightScope.PracticeSession.ToCode()
                    && i.PracticeSessionId == sessionId, cancellationToken);

            if (concurrentExisting is not null)
            {
                var recos = await LoadRecommendationsAsync(concurrentExisting.InsightId, userId, cancellationToken);
                return ToInsightView(concurrentExisting, recos);
            }
            throw;
        }
    }

    public async Task<AiInsightView> GetOrCreateExamInsightAsync(int userId, int attemptId, CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .Include(a => a.Exam)
            .FirstOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken);

        if (attempt is null)
        {
            throw new AiInsightException(ApiError.Codes.AttemptNotFound, $"Exam attempt {attemptId} was not found.");
        }

        if (attempt.Status != AttemptStatus.Graded.ToCode())
        {
            throw new AiInsightException(ApiError.Codes.AttemptNotFinished, $"Exam attempt {attemptId} is not graded yet.");
        }

        var answers = await db.AttemptAnswers
            .AsNoTracking()
            .Where(a => a.AttemptId == attemptId)
            .OrderBy(a => a.QuestionId)
            .ToListAsync(cancellationToken);

        var fingerprint = ComputeExamFingerprint(userId, attemptId, attempt.CertId, attempt.ExamId, attempt.TotalScore, answers);

        var existing = await db.AiInsights
            .FirstOrDefaultAsync(i => i.UserId == userId
                && i.Scope == AiInsightScope.Attempt.ToCode()
                && i.AttemptId == attemptId, cancellationToken);

        if (existing is not null)
        {
            var recos = await LoadRecommendationsAsync(existing.InsightId, userId, cancellationToken);
            return ToInsightView(existing, recos);
        }

        var existingFingerprint = await db.AiInsights
            .FirstOrDefaultAsync(i => i.UserId == userId
                && i.Scope == AiInsightScope.Attempt.ToCode()
                && i.SourceFingerprint == fingerprint, cancellationToken);

        if (existingFingerprint is not null)
        {
            var recos = await LoadRecommendationsAsync(existingFingerprint.InsightId, userId, cancellationToken);
            return ToInsightView(existingFingerprint, recos);
        }

        var newInsight = new AiInsight
        {
            UserId = userId,
            CertId = attempt.CertId,
            Scope = AiInsightScope.Attempt.ToCode(),
            AttemptId = attemptId,
            PracticeSessionId = null,
            LearnerPathId = null,
            Status = AiInsightStatus.Pending.ToCode(),
            SourceFingerprint = fingerprint,
            CreatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            db.AiInsights.Add(newInsight);
            await db.SaveChangesAsync(cancellationToken);
            return ToInsightView(newInsight, Array.Empty<RecommendationItem>());
        }
        catch (DbUpdateException)
        {
            db.Entry(newInsight).State = EntityState.Detached;
            var concurrentExisting = await db.AiInsights
                .FirstOrDefaultAsync(i => i.UserId == userId
                    && i.Scope == AiInsightScope.Attempt.ToCode()
                    && i.AttemptId == attemptId, cancellationToken);

            if (concurrentExisting is not null)
            {
                var recos = await LoadRecommendationsAsync(concurrentExisting.InsightId, userId, cancellationToken);
                return ToInsightView(concurrentExisting, recos);
            }
            throw;
        }
    }

    public async Task RetryPracticeInsightAsync(int userId, int sessionId, CancellationToken cancellationToken = default)
    {
        var insight = await db.AiInsights
            .FirstOrDefaultAsync(i => i.UserId == userId
                && i.Scope == AiInsightScope.PracticeSession.ToCode()
                && i.PracticeSessionId == sessionId, cancellationToken);

        if (insight is null)
        {
            throw new AiInsightException(ApiError.Codes.InsightNotFound, $"Insight for practice session {sessionId} was not found.");
        }

        await RetryInsightInternalAsync(insight, cancellationToken);
    }

    public async Task RetryExamInsightAsync(int userId, int attemptId, CancellationToken cancellationToken = default)
    {
        var insight = await db.AiInsights
            .FirstOrDefaultAsync(i => i.UserId == userId
                && i.Scope == AiInsightScope.Attempt.ToCode()
                && i.AttemptId == attemptId, cancellationToken);

        if (insight is null)
        {
            throw new AiInsightException(ApiError.Codes.InsightNotFound, $"Insight for exam attempt {attemptId} was not found.");
        }

        await RetryInsightInternalAsync(insight, cancellationToken);
    }

    public async Task RetryInsightAsync(int userId, int insightId, CancellationToken cancellationToken = default)
    {
        var insight = await db.AiInsights
            .FirstOrDefaultAsync(i => i.UserId == userId && i.InsightId == insightId, cancellationToken);

        if (insight is null)
        {
            throw new AiInsightException(ApiError.Codes.InsightNotFound, $"Insight {insightId} was not found.");
        }

        await RetryInsightInternalAsync(insight, cancellationToken);
    }

    private async Task RetryInsightInternalAsync(AiInsight insight, CancellationToken cancellationToken)
    {
        if (insight.Status != AiInsightStatus.Failed.ToCode())
        {
            throw new AiInsightException(ApiError.Codes.InsightNotFailed, "Only failed insights can be retried.");
        }

        RetryCounts.AddOrUpdate(insight.InsightId, 1, (_, count) => count + 1);
        insight.Status = AiInsightStatus.Pending.ToCode();
        insight.FailureReason = null;
        insight.CompletedAt = null;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ProcessPendingInsightsAsync(CancellationToken cancellationToken = default)
    {
        var pendingInsights = await db.AiInsights
            .Where(i => i.Status == AiInsightStatus.Pending.ToCode())
            .OrderBy(i => i.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        if (pendingInsights.Count == 0) return 0;

        int processed = 0;
        foreach (var insight in pendingInsights)
        {
            try
            {
                var attemptCount = RetryCounts.TryGetValue(insight.InsightId, out var count) ? count + 1 : 1;
                AiInsightGenerationRequest? request = null;

                if (insight.Scope == AiInsightScope.PracticeSession.ToCode() && insight.PracticeSessionId.HasValue)
                {
                    request = await BuildPracticeRequestAsync(insight, attemptCount, cancellationToken);
                }
                else if (insight.Scope == AiInsightScope.Attempt.ToCode() && insight.AttemptId.HasValue)
                {
                    request = await BuildExamAttemptRequestAsync(insight, attemptCount, cancellationToken);
                }

                if (request is null)
                {
                    insight.Status = AiInsightStatus.Failed.ToCode();
                    insight.FailureReason = "Unsupported insight scope or source record missing.";
                    insight.CompletedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                    processed++;
                    continue;
                }

                var result = await generator.GenerateInsightAsync(request, cancellationToken);

                if (result.Success && !string.IsNullOrWhiteSpace(result.Narrative))
                {
                    insight.Status = AiInsightStatus.Done.ToCode();
                    insight.Narrative = result.Narrative;
                    insight.Trend = result.Trend?.ToCode();
                    insight.Model = result.Model ?? FakeAiInsightGenerator.DefaultModel;
                    insight.PromptTokens = result.PromptTokens ?? 140;
                    insight.CompletionTokens = result.CompletionTokens ?? 110;
                    insight.FailureReason = null;
                    insight.CompletedAt = DateTimeOffset.UtcNow;

                    // Remove existing recommendations for this insight
                    var existingRecos = await db.Recommendations
                        .Where(r => r.InsightId == insight.InsightId)
                        .ToListAsync(cancellationToken);
                    db.Recommendations.RemoveRange(existingRecos);

                    // Fetch valid target bands and skills for this certificate to protect FK constraints
                    var validBandCodes = await db.ScoreBands
                        .AsNoTracking()
                        .Where(sb => sb.CertId == insight.CertId)
                        .Select(sb => sb.Code)
                        .ToListAsync(cancellationToken);

                    var validSkillIds = await db.Skills
                        .AsNoTracking()
                        .Where(s => s.CertId == insight.CertId)
                        .Select(s => s.SkillId)
                        .ToListAsync(cancellationToken);

                    foreach (var reco in result.Recommendations)
                    {
                        var targetBandCode = reco.TargetBandCode != null && validBandCodes.Contains(reco.TargetBandCode)
                            ? reco.TargetBandCode
                            : null;

                        var skillId = reco.SkillId.HasValue && validSkillIds.Contains(reco.SkillId.Value)
                            ? reco.SkillId
                            : null;

                        db.Recommendations.Add(new Recommendation
                        {
                            UserId = insight.UserId,
                            CertId = insight.CertId,
                            InsightId = insight.InsightId,
                            GradingId = null,
                            PracticeSessionId = insight.PracticeSessionId,
                            AttemptId = insight.AttemptId,
                            SkillId = skillId,
                            Text = reco.Text,
                            Severity = reco.Severity.ToCode(),
                            TargetBandCode = targetBandCode,
                            IsResolved = false,
                            CreatedAt = DateTimeOffset.UtcNow
                        });
                    }
                }
                else
                {
                    insight.Status = AiInsightStatus.Failed.ToCode();
                    insight.FailureReason = Truncate(result.FailureReason ?? "Failed to generate AI insight narrative.", 400);
                    insight.CompletedAt = DateTimeOffset.UtcNow;
                }

                await db.SaveChangesAsync(cancellationToken);
                processed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process AI insight {InsightId}", insight.InsightId);
                insight.Status = AiInsightStatus.Failed.ToCode();
                insight.FailureReason = Truncate(ex.Message, 400);
                insight.CompletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                processed++;
            }
        }

        return processed;
    }

    private async Task<AiInsightGenerationRequest?> BuildPracticeRequestAsync(AiInsight insight, int attemptCount, CancellationToken cancellationToken)
    {
        var session = await db.PracticeSessions
            .AsNoTracking()
            .Include(s => s.Section)
            .FirstOrDefaultAsync(s => s.SessionId == insight.PracticeSessionId!.Value, cancellationToken);

        if (session is null) return null;

        var cert = await db.Certificates
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CertId == insight.CertId, cancellationToken);
        var certName = cert?.Name ?? string.Empty;

        var answers = await db.PracticeAnswers
            .AsNoTracking()
            .Include(pa => pa.Question)
                .ThenInclude(q => q.Category)
            .Where(pa => pa.SessionId == session.SessionId)
            .OrderBy(pa => pa.DisplayOrder)
            .ToListAsync(cancellationToken);

        var questionIds = answers.Select(a => a.QuestionId).Distinct().ToList();
        var allOptions = await db.QuestionOptions
            .AsNoTracking()
            .Where(o => questionIds.Contains(o.QuestionId))
            .ToListAsync(cancellationToken);

        var questionDetails = answers.Select(a =>
        {
            var q = a.Question;
            var qOptions = allOptions.Where(o => o.QuestionId == q.QuestionId).ToList();
            var selectedOpt = a.SelectedOptionId.HasValue
                ? qOptions.FirstOrDefault(o => o.OptionId == a.SelectedOptionId.Value)
                : null;
            var correctOpt = qOptions.FirstOrDefault(o => o.IsCorrect);

            return new InsightQuestionDetail(
                q.QuestionId,
                q.Stem,
                session.SectionId,
                session.Section.Name,
                q.CategoryId,
                q.Category?.Name,
                a.SelectedOptionId,
                selectedOpt?.Label,
                correctOpt?.Label,
                a.IsCorrect,
                a.HintUsed,
                q.Explanation);
        }).ToList();

        // Query historical accuracy from deterministic SQL views
        var sectionAccuracyRows = await db.vw_UserSectionAccuracyAlls
            .AsNoTracking()
            .Where(v => v.UserId == insight.UserId && v.SectionId == session.SectionId)
            .ToListAsync(cancellationToken);

        var sectionAccuracies = sectionAccuracyRows.Select(r => new InsightSectionAccuracy(
            session.SectionId,
            session.Section.Code,
            session.Section.Name,
            r.Answered ?? 0,
            r.Correct ?? 0,
            (r.Answered ?? 0) > 0 ? (int)Math.Round((double)(r.Correct ?? 0) / r.Answered!.Value * 100) : 0
        )).ToList();

        var categoryIds = answers.Where(a => a.Question.CategoryId.HasValue).Select(a => a.Question.CategoryId!.Value).Distinct().ToList();
        var catAccuracyRows = await db.vw_UserCategoryAccuracies
            .AsNoTracking()
            .Where(v => v.UserId == insight.UserId && v.CategoryId != null && categoryIds.Contains(v.CategoryId.Value))
            .ToListAsync(cancellationToken);

        var categoryAccuracies = new List<InsightCategoryAccuracy>();
        var categories = await db.QuestionCategories
            .AsNoTracking()
            .Where(c => categoryIds.Contains(c.CategoryId))
            .ToListAsync(cancellationToken);

        foreach (var c in categories)
        {
            var row = catAccuracyRows.FirstOrDefault(r => r.CategoryId == c.CategoryId);
            var answered = row?.Answered ?? 0;
            var correct = row?.Correct ?? 0;
            var pct = answered > 0 ? (int)Math.Round((double)correct / answered * 100) : 0;
            categoryAccuracies.Add(new InsightCategoryAccuracy(c.CategoryId, c.Code, c.Name, answered, correct, pct));
        }

        // Query user's target band if set
        var targetBand = await db.TargetBands
            .AsNoTracking()
            .Include(tb => tb.ScoreBand)
            .FirstOrDefaultAsync(tb => tb.UserId == insight.UserId && tb.CertId == insight.CertId, cancellationToken);

        var answeredCount = answers.Count(a => a.SelectedOptionId.HasValue || a.IsCorrect.HasValue);
        var correctCount = answers.Count(a => a.IsCorrect == true);
        var hintUsedCount = answers.Count(a => a.HintUsed);
        var correctWithHintCount = answers.Count(a => a.HintUsed && a.IsCorrect == true);

        return new AiInsightGenerationRequest(
            insight.UserId,
            insight.CertId,
            certName,
            AiInsightScope.PracticeSession,
            session.SessionId,
            null,
            session.Section.Name,
            session.QuestionCount,
            answeredCount,
            correctCount,
            hintUsedCount,
            correctWithHintCount,
            targetBand?.TargetBandCode,
            targetBand?.ScoreBand?.Name,
            null,
            null,
            questionDetails,
            sectionAccuracies,
            categoryAccuracies,
            attemptCount);
    }

    private async Task<AiInsightGenerationRequest?> BuildExamAttemptRequestAsync(AiInsight insight, int attemptCount, CancellationToken cancellationToken)
    {
        var attempt = await db.ExamAttempts
            .AsNoTracking()
            .Include(a => a.Exam)
            .Include(a => a.Cert)
            .FirstOrDefaultAsync(a => a.AttemptId == insight.AttemptId!.Value, cancellationToken);

        if (attempt is null) return null;

        var answers = await db.AttemptAnswers
            .AsNoTracking()
            .Include(a => a.Question)
                .ThenInclude(q => q.Category)
            .Include(a => a.Question)
                .ThenInclude(q => q.Section)
            .Where(a => a.AttemptId == attempt.AttemptId)
            .OrderBy(a => a.QuestionId)
            .ToListAsync(cancellationToken);

        var questionIds = answers.Select(a => a.QuestionId).Distinct().ToList();
        var allOptions = await db.QuestionOptions
            .AsNoTracking()
            .Where(o => questionIds.Contains(o.QuestionId))
            .ToListAsync(cancellationToken);

        var questionDetails = answers.Select(a =>
        {
            var q = a.Question;
            var qOptions = allOptions.Where(o => o.QuestionId == q.QuestionId).ToList();
            var selectedOpt = a.SelectedOptionId.HasValue
                ? qOptions.FirstOrDefault(o => o.OptionId == a.SelectedOptionId.Value)
                : null;
            var correctOpt = qOptions.FirstOrDefault(o => o.IsCorrect);

            return new InsightQuestionDetail(
                q.QuestionId,
                q.Stem,
                q.SectionId,
                q.Section.Name,
                q.CategoryId,
                q.Category?.Name,
                a.SelectedOptionId,
                selectedOpt?.Label,
                correctOpt?.Label,
                a.IsCorrect,
                false,
                q.Explanation);
        }).ToList();

        var sectionIds = answers.Select(a => a.Question.SectionId).Distinct().ToList();
        var sectionAccuracyRows = await db.vw_UserSectionAccuracyAlls
            .AsNoTracking()
            .Where(v => v.UserId == insight.UserId && sectionIds.Contains(v.SectionId))
            .ToListAsync(cancellationToken);

        var sections = await db.Sections
            .AsNoTracking()
            .Where(s => sectionIds.Contains(s.SectionId))
            .ToListAsync(cancellationToken);

        var sectionAccuracies = sections.Select(s =>
        {
            var row = sectionAccuracyRows.FirstOrDefault(r => r.SectionId == s.SectionId);
            var ans = row?.Answered ?? 0;
            var cor = row?.Correct ?? 0;
            var pct = ans > 0 ? (int)Math.Round((double)cor / ans * 100) : 0;
            return new InsightSectionAccuracy(s.SectionId, s.Code, s.Name, ans, cor, pct);
        }).ToList();

        var categoryIds = answers.Where(a => a.Question.CategoryId.HasValue).Select(a => a.Question.CategoryId!.Value).Distinct().ToList();
        var catAccuracyRows = await db.vw_UserCategoryAccuracies
            .AsNoTracking()
            .Where(v => v.UserId == insight.UserId && v.CategoryId != null && categoryIds.Contains(v.CategoryId.Value))
            .ToListAsync(cancellationToken);

        var categories = await db.QuestionCategories
            .AsNoTracking()
            .Where(c => categoryIds.Contains(c.CategoryId))
            .ToListAsync(cancellationToken);

        var categoryAccuracies = categories.Select(c =>
        {
            var row = catAccuracyRows.FirstOrDefault(r => r.CategoryId == c.CategoryId);
            var ans = row?.Answered ?? 0;
            var cor = row?.Correct ?? 0;
            var pct = ans > 0 ? (int)Math.Round((double)cor / ans * 100) : 0;
            return new InsightCategoryAccuracy(c.CategoryId, c.Code, c.Name, ans, cor, pct);
        }).ToList();

        var targetBand = await db.TargetBands
            .AsNoTracking()
            .Include(tb => tb.ScoreBand)
            .FirstOrDefaultAsync(tb => tb.UserId == insight.UserId && tb.CertId == insight.CertId, cancellationToken);

        var answeredCount = answers.Count(a => a.SelectedOptionId.HasValue || a.IsCorrect.HasValue);
        var correctCount = answers.Count(a => a.IsCorrect == true);

        return new AiInsightGenerationRequest(
            insight.UserId,
            insight.CertId,
            attempt.Cert.Name,
            AiInsightScope.Attempt,
            null,
            attempt.AttemptId,
            attempt.Exam.Name,
            answers.Count,
            answeredCount,
            correctCount,
            0,
            0,
            targetBand?.TargetBandCode,
            targetBand?.ScoreBand?.Name,
            attempt.TotalScore,
            attempt.BandCode,
            questionDetails,
            sectionAccuracies,
            categoryAccuracies,
            attemptCount);
    }

    private async Task<IReadOnlyList<RecommendationItem>> LoadRecommendationsAsync(int insightId, int userId, CancellationToken cancellationToken)
    {
        var recos = await db.Recommendations
            .AsNoTracking()
            .Include(r => r.Skill)
            .Where(r => r.InsightId == insightId && r.UserId == userId)
            .OrderBy(r => r.RecId)
            .ToListAsync(cancellationToken);

        return recos.Select(r => new RecommendationItem(
            r.RecId,
            r.Text,
            EnumCodes.Parse<RecommendationSeverity>(r.Severity),
            r.TargetBandCode,
            r.Skill?.Name,
            r.CreatedAt,
            r.IsResolved
        )).ToList();
    }

    private static AiInsightView ToInsightView(AiInsight entity, IReadOnlyList<RecommendationItem> recommendations)
    {
        return new AiInsightView(
            entity.InsightId,
            entity.UserId,
            entity.CertId,
            EnumCodes.Parse<AiInsightScope>(entity.Scope),
            entity.PracticeSessionId,
            entity.AttemptId,
            entity.Narrative,
            entity.Trend is not null ? EnumCodes.Parse<AiInsightTrend>(entity.Trend) : null,
            EnumCodes.Parse<AiInsightStatus>(entity.Status),
            entity.FailureReason,
            entity.Model,
            entity.PromptTokens,
            entity.CompletionTokens,
            entity.SourceFingerprint,
            entity.CreatedAt,
            entity.CompletedAt,
            recommendations
        );
    }

    public static string ComputePracticeFingerprint(int userId, int sessionId, int certId, int sectionId, IReadOnlyList<PracticeAnswer> answers)
    {
        var answerPart = string.Join(";", answers.Select(a => $"{a.QuestionId}:{a.SelectedOptionId}:{a.IsCorrect}:{a.HintUsed}"));
        var raw = $"practice_session:{userId}:{sessionId}:{certId}:{sectionId}:{answerPart}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    public static string ComputeExamFingerprint(int userId, int attemptId, int certId, int examId, int? totalScore, IReadOnlyList<AttemptAnswer> answers)
    {
        var answerPart = string.Join(";", answers.Select(a => $"{a.QuestionId}:{a.SelectedOptionId}:{a.IsCorrect}"));
        var raw = $"attempt:{userId}:{attemptId}:{certId}:{examId}:{totalScore}:{answerPart}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}
