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
            .OrderBy(a => a.ExamItemId)
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

    /// <summary>
    /// #186: the text of the option a pinned answer named. A version stores labels A–D with no option
    /// rows, so the label selects one of its four columns; anything else is no choice at all.
    /// </summary>
    private static string? PinnedOptionText(ItemVersion version, string? label) => label switch
    {
        "A" => version.OptionA,
        "B" => version.OptionB,
        "C" => version.OptionC,
        "D" => version.OptionD,
        _ => null,
    };

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
            .Include(a => a.ItemVersion)
                .ThenInclude(v => v!.Item)
                    .ThenInclude(i => i.Topic)
            .Where(a => a.AttemptId == attempt.AttemptId)
            .OrderBy(a => a.ExamItemId)
            .ToListAsync(cancellationToken);

        // #269: the section an answer belongs to is its roster position's section.
        var sectionIdByAnswer = await RosterSkills.ByAnswerAsync(db, attempt.AttemptId, cancellationToken);
        var sectionIds = answers
            .Where(a => sectionIdByAnswer.ContainsKey(a.AnswerId))
            .Select(a => sectionIdByAnswer[a.AnswerId])
            .Distinct()
            .ToList();

        var sections = await db.Sections
            .AsNoTracking()
            .Where(s => sectionIds.Contains(s.SectionId))
            .ToListAsync(cancellationToken);
        var sectionById = sections.ToDictionary(s => s.SectionId);

        var questionDetails = answers.Select(a =>
        {
            // #269: the payload describes what the LEARNER SAW, so an answer reads its pinned version.
            var pinned = a.ItemVersion;
            var sectionId = sectionIdByAnswer.TryGetValue(a.AnswerId, out var resolved)
                ? (int?)resolved
                : null;
            var section = sectionId is int id ? sectionById.GetValueOrDefault(id) : null;
            var selectedText = pinned is not null ? PinnedOptionText(pinned, a.SelectedLabel) : null;
            var selectedLabel = pinned is not null ? a.SelectedLabel : null;
            var correctLabel = pinned?.KeyLabel;

            // #271: the cell is the item the version pins - the same join vw_UserTopicAccuracy makes.
            var item = pinned?.Item;
            var topicId = item?.TopicId;
            var topicName = item?.Topic?.Name;
            var level = item?.CefrLevel;

            return new InsightQuestionDetail(
                null,
                pinned?.Stem ?? string.Empty,
                sectionId,
                section?.Name ?? string.Empty,
                topicId,
                topicName,
                level,
                a.SelectedOptionId,
                selectedLabel,
                correctLabel,
                a.IsCorrect,
                false,
                pinned?.Explanation);
        }).ToList();

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

        // #271: the topic axis, from the view that computes it. The weakness the narrative ranks and
        // the cell an error is attributed to are the same rows, so a screened figure and a narrated
        // figure cannot disagree.
        var topicAccuracies = (await TopicAccuracyReader.ReadAsync(db, insight.UserId, insight.CertId, cancellationToken))
            .Select(c => new InsightTopicAccuracy(
                c.TopicId, c.TopicCode, c.TopicName, c.CefrLevel.Code, c.Answered, c.Correct, c.AccuracyPercent))
            .ToList();

        var targetBand = await db.TargetBands
            .AsNoTracking()
            .Include(tb => tb.ScoreBand)
            .FirstOrDefaultAsync(tb => tb.UserId == insight.UserId && tb.CertId == insight.CertId, cancellationToken);

        // #253: one definition. This counted an answered writing response as skipped, because it read
        // the ungraded IsCorrect as "no answer".
        var answeredCount = answers.Count(a =>
            AnswerContent.IsAnswered(a.SelectedOptionId, a.SelectedLabel, a.ResponseText, a.SelectedOptionIdsJson));
        var correctCount = answers.Count(a => a.IsCorrect == true);
        var errorFingerprints = ExtractErrorFingerprints(answers, topicAccuracies);
        var writingEvidences = await ExtractWritingEvidenceAsync(attempt.AttemptId, cancellationToken);

        var facts = await BuildFactsAsync(insight.UserId, insight.CertId, targetBand, cancellationToken);

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
            topicAccuracies,
            attemptCount,
            ErrorFingerprints: errorFingerprints,
            WritingEvidences: writingEvidences,
            Facts: facts);
    }
}
