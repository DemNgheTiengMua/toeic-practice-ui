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
                else if (insight.Scope == AiInsightScope.Dashboard.ToCode())
                {
                    request = await BuildDashboardRequestAsync(insight, attemptCount, cancellationToken);
                }
                else if (insight.Scope == AiInsightScope.Path.ToCode() && insight.LearnerPathId.HasValue)
                {
                    request = await BuildPathRequestAsync(insight, attemptCount, cancellationToken);
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
}
