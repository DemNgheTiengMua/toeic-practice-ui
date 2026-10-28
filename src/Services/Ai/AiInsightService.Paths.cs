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

    public async Task<AiInsightView> GetOrCreatePathInsightAsync(int userId, CancellationToken cancellationToken = default)
    {
        var activeCertId = await _learners.GetActiveCertIdAsync(userId, cancellationToken);
        var activePath = await db.LearnerPaths
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId && p.CertId == activeCertId && p.Status == "active", cancellationToken);

        if (activePath is null)
        {
            throw new AiInsightException(ApiError.Codes.LearningPathNotFound, "Active learning path not found.");
        }

        var completedSteps = await db.LearnerPathSteps
            .AsNoTracking()
            .Where(s => s.LearnerPathId == activePath.LearnerPathId && s.Status == "done")
            .OrderByDescending(s => s.CompletedAt ?? s.StartedAt)
            .ThenByDescending(s => s.StepId)
            .ToListAsync(cancellationToken);

        int completedStepCount = completedSteps.Count;
        int? latestCompletedStepId = completedSteps.FirstOrDefault()?.StepId;

        var fingerprint = ComputePathFingerprint(userId, activePath.CertId, activePath.LearnerPathId, completedStepCount, latestCompletedStepId);

        var existingFingerprint = await db.AiInsights
            .FirstOrDefaultAsync(i => i.UserId == userId
                && i.Scope == AiInsightScope.Path.ToCode()
                && i.SourceFingerprint == fingerprint, cancellationToken);

        if (existingFingerprint is not null)
        {
            var recos = await LoadRecommendationsAsync(existingFingerprint.InsightId, userId, cancellationToken);
            return ToInsightView(existingFingerprint, recos);
        }

        var newInsight = new AiInsight
        {
            UserId = userId,
            CertId = activePath.CertId,
            Scope = AiInsightScope.Path.ToCode(),
            LearnerPathId = activePath.LearnerPathId,
            PracticeSessionId = null,
            AttemptId = null,
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
                    && i.Scope == AiInsightScope.Path.ToCode()
                    && i.SourceFingerprint == fingerprint, cancellationToken);

            if (concurrentExisting is not null)
            {
                var recos = await LoadRecommendationsAsync(concurrentExisting.InsightId, userId, cancellationToken);
                return ToInsightView(concurrentExisting, recos);
            }
            throw;
        }
    }

    private async Task<AiInsightGenerationRequest?> BuildPathRequestAsync(AiInsight insight, int attemptCount, CancellationToken cancellationToken)
    {
        var path = await db.LearnerPaths
            .AsNoTracking()
            .Include(p => p.PathTemplate)
            .Include(p => p.Cert)
            .FirstOrDefaultAsync(p => p.LearnerPathId == insight.LearnerPathId!.Value, cancellationToken);

        if (path is null) return null;

        var certName = path.Cert.Name;

        var allSteps = await db.LearnerPathSteps
            .AsNoTracking()
            .Include(s => s.PathModuleItem)
                .ThenInclude(pmi => pmi.PathModule)
                    .ThenInclude(pm => pm.Skill)
            .Where(s => s.LearnerPathId == path.LearnerPathId)
            .OrderBy(s => s.PathModuleItem.PathModule.DisplayOrder)
            .ThenBy(s => s.PathModuleItem.DisplayOrder)
            .ToListAsync(cancellationToken);

        int totalSteps = allSteps.Count;
        var completedSteps = allSteps.Where(s => s.Status == "done").ToList();
        int completedCount = completedSteps.Count;

        var targetBand = await db.TargetBands
            .AsNoTracking()
            .Include(tb => tb.ScoreBand)
            .FirstOrDefaultAsync(tb => tb.UserId == insight.UserId && tb.CertId == insight.CertId, cancellationToken);

        var sectionAccuracyRows = await db.vw_UserSectionAccuracyAlls
            .AsNoTracking()
            .Where(v => v.UserId == insight.UserId)
            .ToListAsync(cancellationToken);

        var sections = await db.Sections
            .AsNoTracking()
            .Where(s => s.CertId == insight.CertId)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var sectionAccuracies = sections.Select(s =>
        {
            var row = sectionAccuracyRows.FirstOrDefault(r => r.SectionId == s.SectionId);
            var ans = row?.Answered ?? 0;
            var cor = row?.Correct ?? 0;
            var pct = ans > 0 ? (int)Math.Round((double)cor / ans * 100) : 0;
            return new InsightSectionAccuracy(s.SectionId, s.Code, s.Name, ans, cor, pct);
        }).ToList();

        var categories = await db.QuestionCategories
            .AsNoTracking()
            .Where(c => c.CertId == insight.CertId)
            .ToListAsync(cancellationToken);

        var catIds = categories.Select(c => c.CategoryId).ToList();
        var catAccuracyRows = await db.vw_UserCategoryAccuracies
            .AsNoTracking()
            .Where(v => v.UserId == insight.UserId && v.CategoryId != null && catIds.Contains(v.CategoryId.Value))
            .ToListAsync(cancellationToken);

        var categoryAccuracies = categories.Select(c =>
        {
            var row = catAccuracyRows.FirstOrDefault(r => r.CategoryId == c.CategoryId);
            var ans = row?.Answered ?? 0;
            var cor = row?.Correct ?? 0;
            var pct = ans > 0 ? (int)Math.Round((double)cor / ans * 100) : 0;
            return new InsightCategoryAccuracy(c.CategoryId, c.Code, c.Name, ans, cor, pct);
        }).Where(ca => ca.Answered > 0).ToList();

        var latestPlacement = await db.PlacementTests
            .AsNoTracking()
            .Where(p => p.PlacementId == path.PlacementId)
            .FirstOrDefaultAsync(cancellationToken);

        return new AiInsightGenerationRequest(
            insight.UserId,
            insight.CertId,
            certName,
            AiInsightScope.Path,
            null,
            null,
            $"{path.PathTemplate.FromBandCode} → {path.PathTemplate.ToBandCode} Learning Path",
            totalSteps,
            completedCount,
            completedSteps.Count(s => (s.Score ?? 0) >= 70),
            null,
            null,
            targetBand?.TargetBandCode ?? path.PathTemplate.ToBandCode,
            targetBand?.ScoreBand?.Name ?? path.PathTemplate.ToBandCode,
            latestPlacement?.TotalScore,
            path.CurrentBandCode,
            Array.Empty<InsightQuestionDetail>(),
            sectionAccuracies,
            categoryAccuracies,
            attemptCount,
            LearnerPathId: path.LearnerPathId);
    }
}
