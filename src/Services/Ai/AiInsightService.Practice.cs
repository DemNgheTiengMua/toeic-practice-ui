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
        // D3: a read does not move the state machine. This block finished the session on a GET, so
        // opening the insight panel mid-drill closed it and the learner's next answer was refused.
        // The sitting finishes a session; an insight about it reports what it found.

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
                .ThenInclude(v => v!.Item)
                    .ThenInclude(i => i.Topic)
            .Where(pa => pa.SessionId == session.SessionId)
            .OrderBy(pa => pa.DisplayOrder)
            .ToListAsync(cancellationToken);

        // #187: the payload describes what the learner actually saw, so an answer reads its version.
        var scopeName = session.Section?.Name ?? session.Topic?.Name ?? string.Empty;

        var questionDetails = answers.Select(a =>
        {
            // #271: the cell comes off the item the version pins, which is the join the accuracy view
            // makes - so the detail and the cell the narrative ranks are one attribution.
            var pinned = a.ItemVersion;
            var item = pinned?.Item;

            return new InsightQuestionDetail(
                null,
                pinned?.Stem ?? string.Empty,
                null,
                scopeName,
                item?.TopicId,
                item?.Topic?.Name,
                item?.CefrLevel,
                null,
                a.SelectedLabel,
                pinned?.KeyLabel,
                a.IsCorrect,
                a.HintUsed,
                pinned?.Explanation,
                pinned?.ItemVersionId);
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

        // #271: the topic axis, from the view that computes it. A pool drill's answers are at a cell,
        // not at a category, so this is the whole of the "which topic is weak" block - and the
        // certificate scoping is the reader's, so a cross-skill drill cannot report another skill's
        // cell as this session's.
        var topicAccuracies = (await TopicAccuracyReader.ReadAsync(db, insight.UserId, insight.CertId, cancellationToken))
            .Select(c => new InsightTopicAccuracy(
                c.TopicId, c.TopicCode, c.TopicName, c.CefrLevel.Code, c.Answered, c.Correct, c.AccuracyPercent))
            .ToList();

        // Query user's target band if set
        var targetBand = await db.TargetBands
            .AsNoTracking()
            .Include(tb => tb.ScoreBand)
            .FirstOrDefaultAsync(tb => tb.UserId == insight.UserId && tb.CertId == insight.CertId, cancellationToken);

        // #253: the one definition, so an ungraded pool answer is not reported to the mentor as skipped.
        var answeredCount = answers.Count(a =>
            AnswerContent.IsAnswered(a.SelectedOptionId, a.SelectedLabel, a.ResponseText, a.SelectedOptionIdsJson));
        var correctCount = answers.Count(a => a.IsCorrect == true);
        var hintUsedCount = answers.Count(a => a.HintUsed);
        var correctWithHintCount = answers.Count(a => a.HintUsed && a.IsCorrect == true);

        var facts = await BuildFactsAsync(insight.UserId, insight.CertId, targetBand, cancellationToken);

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
            topicAccuracies,
            attemptCount,
            Facts: facts);
    }
}
