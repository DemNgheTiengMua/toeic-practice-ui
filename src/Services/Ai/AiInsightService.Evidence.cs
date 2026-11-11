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

    /// <summary>
    /// #244: the answers a narrative may quote, worst first. The order is "flagged, then the learner's
    /// weakest cells", so the five chosen are the ones a mentor would open first.
    /// <para>
    /// <b>#271: the ranking reads topic cells, and an answer joins its cell on both axes.</b> It used to
    /// rank by <c>Question.CategoryId</c> against the retired category view. It cannot now: the answer's
    /// cell is the ITEM's <c>(TopicId, CefrLevel)</c> - the same rule <c>vw_UserTopicAccuracy</c> uses,
    /// so the ranking cannot credit an answer to a topic it was not asked in. An answer with no cell
    /// (legacy, or a paper the pool has not assembled) ranks last rather than crashing, which is why
    /// the index lookup keeps its -1 guard.
    /// </para>
    /// </summary>
    public List<ErrorFingerprint> ExtractErrorFingerprints(
        IReadOnlyList<AttemptAnswer> answers,
        IReadOnlyList<QuestionOption> allOptions,
        IReadOnlyList<InsightTopicAccuracy> topicAccuracies)
    {
        var incorrectAnswers = answers.Where(a => a.IsCorrect == false).ToList();
        if (incorrectAnswers.Count == 0) return new List<ErrorFingerprint>();

        var flaggedErrors = incorrectAnswers.Where(a => a.IsFlagged).ToList();
        var weakestFirst = topicAccuracies
            .OrderBy(t => t.AccuracyPercentage)
            .ThenByDescending(t => t.Answered - t.Correct)
            .ToList();

        // An answer with no cell ranks last; -1 means "not in the list", and int.MaxValue keeps such
        // answers behind every ranked one instead of ahead of them.
        int RankOf(AttemptAnswer answer)
        {
            var cell = CellOf(answer.ItemVersion);
            if (cell is null)
            {
                return int.MaxValue;
            }

            var index = weakestFirst.FindIndex(t =>
                t.TopicId == cell.Value.TopicId && t.LevelCode == cell.Value.LevelCode);
            return index < 0 ? int.MaxValue : index;
        }

        var lowestCellErrors = incorrectAnswers
            .Where(a => !a.IsFlagged)
            .OrderBy(RankOf)
            .ToList();

        var selected = flaggedErrors.Concat(lowestCellErrors).Take(5).ToList();
        if (selected.Count < 3)
        {
            var remaining = incorrectAnswers.Except(selected).Take(5 - selected.Count);
            selected.AddRange(remaining);
        }

        var result = new List<ErrorFingerprint>();
        foreach (var ans in selected)
        {
            // #269 / #271: a pool answer has no Question row, so `q` may be null. This loop already
            // read `q.QuestionId` unconditionally, which threw for every pool answer - the exam insight
            // could not be generated for a paper assembled from the pool.
            var q = ans.Question;
            var qOpts = allOptions.Where(o => o.QuestionId == q.QuestionId).ToList();
            // #186: a pinned answer names a label on its version; the legacy row has no option to look
            // up. The fingerprint describes what the learner saw, so the version supplies it.
            var pinned = ans.ItemVersion;
            var chosen = pinned is not null
                ? PinnedOptionText(pinned, ans.SelectedLabel)
                : ans.SelectedOptionId.HasValue
                    ? qOpts.FirstOrDefault(o => o.OptionId == ans.SelectedOptionId.Value)?.Text
                    : null;
            var correct = pinned is not null
                ? PinnedOptionText(pinned, pinned.KeyLabel)
                : qOpts.FirstOrDefault(o => o.IsCorrect)?.Text;

            // The cell's NAME comes from the payload's own row, not from a second lookup: the row the
            // answer was ranked against is the one it is described by. A cell absent from the payload
            // (impossible for a graded attempt, since both read the same view) names nothing rather
            // than inventing a label.
            var cell = CellOf(pinned);
            var cellName = cell is null
                ? null
                : weakestFirst.FirstOrDefault(t =>
                    t.TopicId == cell.Value.TopicId && t.LevelCode == cell.Value.LevelCode) is { } row
                        ? $"{row.TopicName} at {row.LevelCode}"
                        : null;

            result.Add(new ErrorFingerprint(
                pinned?.Stem ?? q?.Stem ?? string.Empty,
                chosen,
                correct,
                pinned?.Explanation ?? q?.Explanation,
                ans.IsFlagged,
                cellName));
        }

        return result;
    }

    /// <summary>
    /// #271: the <c>(topic, level)</c> cell an answer belongs to, read from the item its version pins -
    /// the same join <c>vw_UserTopicAccuracy</c> makes, so an answer and its accuracy row are attributed
    /// identically. Null when the answer pins no version (a legacy answer), which has no cell at all
    /// rather than a guessed one.
    /// </summary>
    private static (int TopicId, string TopicName, string LevelCode)? CellOf(ItemVersion? version)
    {
        var item = version?.Item;
        if (item is null)
        {
            return null;
        }

        return (item.TopicId, item.Topic?.Name ?? string.Empty, item.CefrLevel.Code);
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
                fr.QuestionId!.Value,
                fr.Question?.Stem ?? "Writing Prompt",
                text,
                words,
                scores,
                comments));
        }

        return result;
    }
}
