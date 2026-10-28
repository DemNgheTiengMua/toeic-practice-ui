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
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Services;

public sealed partial class AiInsightService
{

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
            recommendations,
            entity.LearnerPathId
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

    public static string ComputeDashboardFingerprint(
        int userId,
        int certId,
        int? latestAttemptId,
        DateTimeOffset? latestAttemptTime,
        int gradedAttemptCount,
        int? latestSessionId,
        DateTimeOffset? latestSessionTime,
        int finishedPracticeCount,
        int totalAnswered,
        int totalCorrect)
    {
        var attemptTimeStr = latestAttemptTime?.ToUnixTimeSeconds().ToString() ?? "none";
        var sessionTimeStr = latestSessionTime?.ToUnixTimeSeconds().ToString() ?? "none";
        var raw = $"dashboard:{userId}:{certId}:{latestAttemptId?.ToString() ?? "none"}:{attemptTimeStr}:{gradedAttemptCount}:{latestSessionId?.ToString() ?? "none"}:{sessionTimeStr}:{finishedPracticeCount}:{totalAnswered}:{totalCorrect}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    public static string ComputePathFingerprint(int userId, int certId, int learnerPathId, int completedStepCount, int? latestCompletedStepId)
    {
        var latestStepStr = latestCompletedStepId?.ToString() ?? "none";
        var raw = $"path:{userId}:{certId}:{learnerPathId}:{completedStepCount}:{latestStepStr}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}
