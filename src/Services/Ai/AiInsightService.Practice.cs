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
            .Include(pa => pa.ItemVersion)
            .Where(pa => pa.SessionId == sessionId)
            .OrderBy(pa => pa.DisplayOrder)
            .ToListAsync(cancellationToken);

        var fingerprint = ComputePracticeFingerprint(userId, sessionId, session.CertId, session.SectionId, answers);

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
            CertId = session.CertId,
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

    private async Task<AiInsightGenerationRequest?> BuildPracticeRequestAsync(AiInsight insight, int attemptCount, CancellationToken cancellationToken)
    {
        var session = await db.PracticeSessions
            .AsNoTracking()
            .Include(s => s.Section)
            .Include(s => s.Topic)
            .FirstOrDefaultAsync(s => s.SessionId == insight.PracticeSessionId!.Value, cancellationToken);

        if (session is null) return null;

        var cert = await db.Certificates
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CertId == insight.CertId, cancellationToken);
        var certName = cert?.Name ?? string.Empty;

        var answers = await db.PracticeAnswers
            .AsNoTracking()
            .Include(pa => pa.ItemVersion)
            .Include(pa => pa.Question)
                .ThenInclude(q => q!.Category)
            .Where(pa => pa.SessionId == session.SessionId)
            .OrderBy(pa => pa.DisplayOrder)
            .ToListAsync(cancellationToken);

        var questionIds = answers.Where(a => a.QuestionId.HasValue).Select(a => a.QuestionId!.Value).Distinct().ToList();
        var allOptions = questionIds.Count == 0
            ? []
            : await db.QuestionOptions
                .AsNoTracking()
                .Where(o => questionIds.Contains(o.QuestionId))
                .ToListAsync(cancellationToken);

        // #187: the payload describes what the learner actually saw, so a pinned answer reads its
        // version. An unpinned answer is a legacy one and reads the question row it was answered from.
        var scopeName = session.Section?.Name ?? session.Topic?.Name ?? string.Empty;

        var questionDetails = answers.Select(a =>
        {
            var q = a.Question;
            var pinned = a.ItemVersion;

            if (pinned is not null)
            {
                return new InsightQuestionDetail(
                    null,
                    pinned.Stem,
                    null,
                    scopeName,
                    null,
                    null,
                    null,
                    a.SelectedLabel,
                    pinned.KeyLabel,
                    a.IsCorrect,
                    a.HintUsed,
                    pinned.Explanation,
                    pinned.ItemVersionId);
            }

            var qOptions = allOptions.Where(o => o.QuestionId == q!.QuestionId).ToList();
            var selectedOpt = a.SelectedOptionId.HasValue
                ? qOptions.FirstOrDefault(o => o.OptionId == a.SelectedOptionId.Value)
                : null;
            var correctOpt = qOptions.FirstOrDefault(o => o.IsCorrect);

            // #187: the session's own section is the question's section - FK_PracticeAnswers_QuestionSection
            // makes that true - so the label is read from the session that was already loaded rather
            // than from a navigation this query does not include.
            return new InsightQuestionDetail(
                q!.QuestionId,
                q.Stem,
                q.SectionId,
                scopeName,
                q.CategoryId,
                q.Category?.Name,
                a.SelectedOptionId,
                selectedOpt?.Label,
                correctOpt?.Label,
                a.IsCorrect,
                a.HintUsed,
                q.Explanation);
        }).ToList();

        // Query historical accuracy from deterministic SQL views. A pool drill has no section, so it
        // contributes no section row: vw_UserSectionAccuracyAll only counts section answers, and
        // inventing a row here would attribute a topic's answers to a section that never saw them.
        var sectionAccuracies = new List<InsightSectionAccuracy>();
        if (session.SectionId is { } sectionId && session.Section is { } sessionSection)
        {
            var sectionAccuracyRows = await db.vw_UserSectionAccuracyAlls
                .AsNoTracking()
                .Where(v => v.UserId == insight.UserId && v.SectionId == sectionId)
                .ToListAsync(cancellationToken);

            sectionAccuracies = sectionAccuracyRows.Select(r => new InsightSectionAccuracy(
                sessionSection.SectionId,
                sessionSection.Code,
                sessionSection.Name,
                r.Answered ?? 0,
                r.Correct ?? 0,
                (r.Answered ?? 0) > 0 ? (int)Math.Round((double)(r.Correct ?? 0) / r.Answered!.Value * 100) : 0
            )).ToList();
        }

        var categoryIds = answers.Where(a => a.Question?.CategoryId is not null).Select(a => a.Question!.CategoryId!.Value).Distinct().ToList();
        var catAccuracyRows = categoryIds.Count == 0
            ? []
            : await db.vw_UserCategoryAccuracies
                .AsNoTracking()
                .Where(v => v.UserId == insight.UserId && v.CategoryId != null && categoryIds.Contains(v.CategoryId.Value))
                .ToListAsync(cancellationToken);

        var categoryAccuracies = new List<InsightCategoryAccuracy>();
        var categories = categoryIds.Count == 0
            ? []
            : await db.QuestionCategories
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

        var answeredCount = answers.Count(a => a.SelectedOptionId.HasValue || a.SelectedLabel is not null || a.IsCorrect.HasValue);
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
            scopeName,
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
}
