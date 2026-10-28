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

    public List<ErrorFingerprint> ExtractErrorFingerprints(
        IReadOnlyList<AttemptAnswer> answers,
        IReadOnlyList<QuestionOption> allOptions,
        IReadOnlyList<vw_UserCategoryAccuracy> catAccuracyRows)
    {
        var incorrectAnswers = answers.Where(a => a.IsCorrect == false).ToList();
        if (incorrectAnswers.Count == 0) return new List<ErrorFingerprint>();

        var flaggedErrors = incorrectAnswers.Where(a => a.IsFlagged).ToList();
        var sortedCategoryIds = catAccuracyRows
            .Where(r => r.CategoryId.HasValue)
            .OrderBy(r => (double)(r.Correct ?? 0) / Math.Max(1, r.Answered ?? 1))
            .Select(r => r.CategoryId!.Value)
            .ToList();

        var lowestCatErrors = incorrectAnswers
            .Where(a => !a.IsFlagged && a.Question.CategoryId.HasValue)
            .OrderBy(a => sortedCategoryIds.IndexOf(a.Question.CategoryId!.Value) is var idx && idx >= 0 ? idx : 999)
            .ToList();

        var selected = flaggedErrors.Concat(lowestCatErrors).Take(5).ToList();
        if (selected.Count < 3)
        {
            var remaining = incorrectAnswers.Except(selected).Take(5 - selected.Count);
            selected.AddRange(remaining);
        }

        var result = new List<ErrorFingerprint>();
        foreach (var ans in selected)
        {
            var q = ans.Question;
            var qOpts = allOptions.Where(o => o.QuestionId == q.QuestionId).ToList();
            var chosen = ans.SelectedOptionId.HasValue
                ? qOpts.FirstOrDefault(o => o.OptionId == ans.SelectedOptionId.Value)?.Text
                : null;
            var correct = qOpts.FirstOrDefault(o => o.IsCorrect)?.Text;

            result.Add(new ErrorFingerprint(
                q.Stem,
                chosen,
                correct,
                q.Explanation,
                ans.IsFlagged,
                q.Category?.Name));
        }

        return result;
    }

    public async Task<List<WritingEvidence>> ExtractWritingEvidenceAsync(
        int attemptId,
        CancellationToken cancellationToken = default)
    {
        var freeResponses = await db.FreeResponses
            .AsNoTracking()
            .Include(fr => fr.Question)
            .Include(fr => fr.AiGradingFreeResponse)
                .ThenInclude(g => g!.AiGradingScores)
                    .ThenInclude(s => s.GradingCriterion)
            .Where(fr => fr.AttemptId == attemptId)
            .ToListAsync(cancellationToken);

        var result = new List<WritingEvidence>();
        foreach (var fr in freeResponses)
        {
            var grading = fr.AiGradingFreeResponse;
            var scores = new List<WritingRubricScore>();
            string? comments = null;

            if (grading != null)
            {
                foreach (var sc in grading.AiGradingScores)
                {
                    scores.Add(new WritingRubricScore(
                        sc.GradingCriterion?.Name ?? "Criterion",
                        sc.Score,
                        sc.GradingCriterion?.MaxScore ?? 5,
                        sc.Comment));
                }
                comments = string.Join("; ", grading.AiGradingScores.Where(s => !string.IsNullOrEmpty(s.Comment)).Select(s => (s.GradingCriterion?.Name ?? "Criterion") + ": " + s.Comment));
            }

            var text = fr.ResponseText ?? string.Empty;
            int words = string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

            result.Add(new WritingEvidence(
                fr.QuestionId,
                fr.Question?.Stem ?? "Writing Prompt",
                text,
                words,
                scores,
                comments));
        }

        return result;
    }
}
