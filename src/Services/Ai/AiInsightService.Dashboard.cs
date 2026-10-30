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
    /// <summary>One gate per stripe of learners, fixed in size so it never grows with the user
    /// table; only learners loading a dashboard at the same moment can wait on each other.</summary>
    private static readonly SemaphoreSlim[] DashboardInsightGates =
        Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async Task<AiInsightView> GetOrCreateDashboardInsightAsync(int userId, int? certId = null, CancellationToken cancellationToken = default)
    {
        int targetCertId;
        if (certId.HasValue)
        {
            var exists = await db.Certificates.AsNoTracking().AnyAsync(c => c.CertId == certId.Value, cancellationToken);
            if (!exists)
            {
                throw new AiInsightException(ApiError.Codes.CertificateUnavailable, $"Certificate {certId.Value} is not available.");
            }
            targetCertId = certId.Value;
        }
        else
        {
            targetCertId = await _learners.GetActiveCertIdAsync(userId, cancellationToken);
        }

        // Query latest graded attempt
        var latestAttempt = await db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.CertId == targetCertId && a.Status == AttemptStatus.Graded.ToCode())
            .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
            .Select(a => new { a.AttemptId, TakenAt = a.SubmittedAt ?? a.StartedAt })
            .FirstOrDefaultAsync(cancellationToken);

        var gradedAttemptCount = await db.ExamAttempts
            .AsNoTracking()
            .CountAsync(a => a.UserId == userId && a.CertId == targetCertId && a.Status == AttemptStatus.Graded.ToCode(), cancellationToken);

        // Query latest finished practice session
        var latestPractice = await db.PracticeSessions
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.Section.CertId == targetCertId && p.Status == PracticeStatus.Finished.ToCode())
            .OrderByDescending(p => p.FinishedAt ?? p.StartedAt)
            .Select(p => new { p.SessionId, FinishedAt = p.FinishedAt ?? p.StartedAt })
            .FirstOrDefaultAsync(cancellationToken);

        var finishedPracticeCount = await db.PracticeSessions
            .AsNoTracking()
            .CountAsync(p => p.UserId == userId && p.Section.CertId == targetCertId && p.Status == PracticeStatus.Finished.ToCode(), cancellationToken);

        // Query section accuracies for accuracy summary numbers
        var sectionIds = await db.Sections
            .AsNoTracking()
            .Where(s => s.CertId == targetCertId)
            .Select(s => s.SectionId)
            .ToListAsync(cancellationToken);

        var accuracyRows = await db.vw_UserSectionAccuracyAlls
            .AsNoTracking()
            .Where(v => v.UserId == userId && sectionIds.Contains(v.SectionId))
            .ToListAsync(cancellationToken);

        var totalAnswered = accuracyRows.Sum(r => r.Answered ?? 0);
        var totalCorrect = accuracyRows.Sum(r => r.Correct ?? 0);

        var fingerprint = ComputeDashboardFingerprint(
            userId,
            targetCertId,
            latestAttempt?.AttemptId,
            latestAttempt?.TakenAt,
            gradedAttemptCount,
            latestPractice?.SessionId,
            latestPractice?.FinishedAt,
            finishedPracticeCount,
            totalAnswered,
            totalCorrect);

        // Serialise the lookup-then-insert per learner. Concurrent first loads (two tabs, a
        // double-clicked sign-in) otherwise all miss the lookup and all insert; every loser hits
        // UX_AiInsights_Fingerprint, which EF logs as a failed save even though the catch in
        // FindOrCreateDashboardInsightAsync recovers. That catch stays for a second API process.
        var gate = DashboardInsightGates[(int)((uint)userId % (uint)DashboardInsightGates.Length)];
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await FindOrCreateDashboardInsightAsync(userId, targetCertId, fingerprint, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<AiInsightView> FindOrCreateDashboardInsightAsync(
        int userId, int targetCertId, string fingerprint, CancellationToken cancellationToken)
    {
        // Check if an insight with this fingerprint exists for this user and scope
        var existingFingerprint = await db.AiInsights
            .FirstOrDefaultAsync(i => i.UserId == userId
                && i.Scope == AiInsightScope.Dashboard.ToCode()
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
            CertId = targetCertId,
            Scope = AiInsightScope.Dashboard.ToCode(),
            PracticeSessionId = null,
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
                    && i.Scope == AiInsightScope.Dashboard.ToCode()
                    && i.SourceFingerprint == fingerprint, cancellationToken);

            if (concurrentExisting is not null)
            {
                var recos = await LoadRecommendationsAsync(concurrentExisting.InsightId, userId, cancellationToken);
                return ToInsightView(concurrentExisting, recos);
            }
            throw;
        }
    }

    private async Task<AiInsightGenerationRequest?> BuildDashboardRequestAsync(AiInsight insight, int attemptCount, CancellationToken cancellationToken)
    {
        var cert = await db.Certificates
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CertId == insight.CertId, cancellationToken);

        var certName = cert?.Name ?? "Dashboard";

        // Query previous completed dashboard insight using index IX_AiInsights_Latest
        var previousInsight = await db.AiInsights
            .AsNoTracking()
            .Where(i => i.UserId == insight.UserId
                && i.CertId == insight.CertId
                && i.Scope == AiInsightScope.Dashboard.ToCode()
                && i.Status == AiInsightStatus.Done.ToCode()
                && i.InsightId != insight.InsightId)
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        PreviousInsightSummary? prevSummary = previousInsight is not null
            ? new PreviousInsightSummary(
                previousInsight.InsightId,
                previousInsight.Narrative,
                previousInsight.Trend != null ? EnumCodes.Parse<AiInsightTrend>(previousInsight.Trend) : null,
                previousInsight.CreatedAt)
            : null;

        var sections = await db.Sections
            .AsNoTracking()
            .Where(s => s.CertId == insight.CertId)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var sectionIds = sections.Select(s => s.SectionId).ToList();

        var sectionAccuracyRows = await db.vw_UserSectionAccuracyAlls
            .AsNoTracking()
            .Where(v => v.UserId == insight.UserId && sectionIds.Contains(v.SectionId))
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

        var latestMock = await db.ExamAttempts
            .AsNoTracking()
            .Where(a => a.UserId == insight.UserId && a.CertId == insight.CertId
                && a.Status == AttemptStatus.Graded.ToCode() && a.Exam.Purpose == ExamPurpose.Mock.ToCode() && a.TotalScore != null)
            .OrderByDescending(a => a.SubmittedAt ?? a.StartedAt)
            .Select(a => new { a.TotalScore, a.BandCode })
            .FirstOrDefaultAsync(cancellationToken);

        var targetBand = await db.TargetBands
            .AsNoTracking()
            .Include(tb => tb.ScoreBand)
            .FirstOrDefaultAsync(tb => tb.UserId == insight.UserId && tb.CertId == insight.CertId, cancellationToken);

        var totalAnswered = sectionAccuracies.Sum(s => s.Answered);
        var totalCorrect = sectionAccuracies.Sum(s => s.Correct);

        return new AiInsightGenerationRequest(
            insight.UserId,
            insight.CertId,
            certName,
            AiInsightScope.Dashboard,
            null,
            null,
            $"{certName} Dashboard",
            totalAnswered,
            totalAnswered,
            totalCorrect,
            null,
            null,
            targetBand?.TargetBandCode,
            targetBand?.ScoreBand?.Name,
            latestMock?.TotalScore,
            latestMock?.BandCode,
            Array.Empty<InsightQuestionDetail>(),
            sectionAccuracies,
            categoryAccuracies,
            attemptCount,
            PreviousInsight: prevSummary);
    }
}
