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
        var errorFingerprints = ExtractErrorFingerprints(answers, allOptions, catAccuracyRows);
        var writingEvidences = await ExtractWritingEvidenceAsync(attempt.AttemptId, cancellationToken);

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
            attemptCount,
            ErrorFingerprints: errorFingerprints,
            WritingEvidences: writingEvidences);
    }
}
