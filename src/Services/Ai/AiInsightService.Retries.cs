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

    public async Task RetryDashboardInsightAsync(int userId, int? certId = null, CancellationToken cancellationToken = default)
    {
        int targetCertId;
        if (certId.HasValue)
        {
            targetCertId = certId.Value;
        }
        else
        {
            targetCertId = await _learners.GetActiveCertIdAsync(userId, cancellationToken);
        }

        var insight = await db.AiInsights
            .Where(i => i.UserId == userId
                && i.CertId == targetCertId
                && i.Scope == AiInsightScope.Dashboard.ToCode())
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (insight is null)
        {
            throw new AiInsightException(ApiError.Codes.InsightNotFound, "Dashboard insight was not found.");
        }

        await RetryInsightInternalAsync(insight, cancellationToken);
    }

    public async Task RetryPathInsightAsync(int userId, CancellationToken cancellationToken = default)
    {
        var activeCertId = await _learners.GetActiveCertIdAsync(userId, cancellationToken);
        var activePath = await db.LearnerPaths
            .FirstOrDefaultAsync(p => p.UserId == userId && p.CertId == activeCertId && p.Status == "active", cancellationToken);

        if (activePath is null)
        {
            throw new AiInsightException(ApiError.Codes.LearningPathNotFound, "Active learning path not found.");
        }

        var insight = await db.AiInsights
            .Where(i => i.UserId == userId
                && i.LearnerPathId == activePath.LearnerPathId
                && i.Scope == AiInsightScope.Path.ToCode())
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (insight is null)
        {
            throw new AiInsightException(ApiError.Codes.InsightNotFound, "Learning path insight was not found.");
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
}
