using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class PracticeService
{
    public async Task<PracticeSessionInfo> StartSessionAsync(int userId, StartPracticeSessionInput input, CancellationToken cancellationToken = default)
    {
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);

        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);

        return input.TopicId is { } topicId
            ? await StartPoolSessionAsync(userId, activeCertId, topicId, input.CefrLevel!.Value, input.ContentTier, cancellationToken)
            : await StartSectionSessionAsync(userId, activeCertId, input.SectionId!.Value, cancellationToken);
    }

    /// <summary>
    /// #187: a drill against a cell. There is no exam and no section behind it: the items come from
    /// the pool at (topic, level) and the session records which version of each item was answered.
    /// </summary>
    private async Task<PracticeSessionInfo> StartPoolSessionAsync(
        int userId, int certId, int topicId, CefrLevel level, string contentTier, CancellationToken cancellationToken)
    {
        var topic = await db.Topics
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.TopicId == topicId, cancellationToken)
            ?? throw new PracticeException(PracticeException.SectionUnavailable, $"Topic {topicId} does not exist.");

        // #150's rule reaches the pool too: speaking is answered by voice, and its recorded, AI-graded
        // practice is a different attempt. Say where it lives rather than serving a textarea.
        if (string.Equals(topic.Modality, Modality.Speaking.ToCode(), StringComparison.Ordinal))
        {
            throw new PracticeException(
                PracticeException.ModalityNotAllowed,
                "Speaking is answered by voice, so it has no typed drill: open the recorded speaking practice, which is graded by AI.");
        }

        var selected = await PlanPoolItemsAsync(userId, topic.Modality, topicId, level, contentTier, cancellationToken);

        if (selected.Count == 0)
        {
            // #190: name the tier. "No active items at R-COH B2" is now an incomplete sentence: the
            // cell may be full of the other tier, and a message that omitted it would send an author
            // looking for a problem in the wrong one.
            throw new PracticeException(
                PracticeException.NoQuestionsAvailable,
                $"No {contentTier} items are active at {topic.Code} {level.Code}.");
        }

        await AbandonInProgressAsync(userId, certId, cancellationToken);

        var session = new PracticeSession
        {
            UserId = userId,
            CertId = certId,
            SectionId = null,
            TopicId = topicId,
            CefrLevel = level,
            Modality = topic.Modality,
            QuestionCount = selected.Count,
            Status = PracticeStatus.InProgress.ToCode(),
            CorrectCount = 0,
            QuestionSource = QuestionSource.All.ToCode(),
            StartedAt = _clock.GetUtcNow(),
        };

        db.PracticeSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        for (var i = 0; i < selected.Count; i++)
        {
            db.PracticeAnswers.Add(new PracticeAnswer
            {
                SessionId = session.SessionId,
                QuestionId = null,
                SectionId = null,
                ItemId = selected[i].ItemId,
                ItemVersionId = selected[i].ItemVersionId,
                SelectedLabel = null,
                SelectedOptionId = null,
                IsCorrect = null,
                HintUsed = false,
                DisplayOrder = i + 1,
            });
        }
        await db.SaveChangesAsync(cancellationToken);

        await PruneOldSessionsAsync(userId, certId, cancellationToken);

        return ToSessionInfo(session, topic.Code, topic.Name);
    }

    private async Task<PracticeSessionInfo> StartSectionSessionAsync(
        int userId, int certId, int sectionId, CancellationToken cancellationToken)
    {
        var section = await db.Sections
            .AsNoTracking()
            .Where(s => s.SectionId == sectionId && s.CertId == certId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PracticeException(PracticeException.SectionUnavailable, $"Section {sectionId} is not available for active certificate.");

        // #150: a spoken answer cannot be typed, and its recorded, AI-graded practice is a different
        // attempt (#151), so speaking is the one modality the free drill does not serve. Say where the
        // recorded practice lives rather than serving a textarea, and keep the refusal here and in
        // DrillableQuestions so the catalog never advertises a drill this method will not start.
        if (string.Equals(section.Modality, Modality.Speaking.ToCode(), StringComparison.Ordinal))
        {
            throw new PracticeException(
                PracticeException.ModalityNotAllowed,
                "Speaking is answered by voice, so it has no typed drill: open the recorded speaking practice, which is graded by AI.");
        }

        var selectedIds = await PlanSmartPracticeAsync(userId, certId, section.SectionId, cancellationToken);

        if (selectedIds.Count == 0)
        {
            throw new PracticeException(PracticeException.NoQuestionsAvailable, "No questions match the selected criteria for this section.");
        }

        await AbandonInProgressAsync(userId, certId, cancellationToken);

        var session = new PracticeSession
        {
            UserId = userId,
            CertId = certId,
            SectionId = section.SectionId,
            Modality = section.Modality,
            QuestionCount = selectedIds.Count,
            Status = PracticeStatus.InProgress.ToCode(),
            CorrectCount = 0,
            QuestionSource = QuestionSource.All.ToCode(),
            StartedAt = _clock.GetUtcNow(),
        };

        db.PracticeSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        for (var i = 0; i < selectedIds.Count; i++)
        {
            db.PracticeAnswers.Add(new PracticeAnswer
            {
                SessionId = session.SessionId,
                QuestionId = selectedIds[i],
                SectionId = section.SectionId,
                SelectedOptionId = null,
                IsCorrect = null,
                HintUsed = false,
                DisplayOrder = i + 1,
            });
        }
        await db.SaveChangesAsync(cancellationToken);

        await PruneOldSessionsAsync(userId, certId, cancellationToken);

        return ToSessionInfo(session, section.Code, section.Name);
    }

    /// <summary>
    /// Clean up or abandon any existing standalone in-progress sessions for this user and certificate.
    /// Shared by both shapes: starting a second drill is one event whether the first was a section or
    /// a cell, so the two must not implement it differently.
    /// </summary>
    private async Task AbandonInProgressAsync(int userId, int certId, CancellationToken cancellationToken)
    {
        var existingInProgress = await db.PracticeSessions
            .Where(s => s.UserId == userId && s.CertId == certId && s.Status == PracticeStatus.InProgress.ToCode() && s.LearnerPathStepId == null)
            .ToListAsync(cancellationToken);

        foreach (var inProg in existingInProgress)
        {
            // #187: a pool answer records a label rather than an option id, so both count as an answer.
            var hasAnswers = await db.PracticeAnswers
                .AnyAsync(pa => pa.SessionId == inProg.SessionId && (pa.SelectedOptionId != null || pa.SelectedLabel != null || pa.IsCorrect != null), cancellationToken);

            if (!hasAnswers)
            {
                var emptyAnswers = await db.PracticeAnswers
                    .Where(pa => pa.SessionId == inProg.SessionId)
                    .ToListAsync(cancellationToken);
                db.PracticeAnswers.RemoveRange(emptyAnswers);
                db.PracticeSessions.Remove(inProg);
            }
            else
            {
                inProg.Status = PracticeStatus.Abandoned.ToCode();
                inProg.FinishedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    /// <summary>
    /// #155: the questions a session contains, decided by mastery state and the cooldown - never by a
    /// learner-picked source. The mix itself is <see cref="SmartPracticePlanner"/>, a pure function; this
    /// only loads what it needs.
    /// </summary>
    private async Task<IReadOnlyList<int>> PlanSmartPracticeAsync(
        int userId, int certId, int sectionId, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();

        var candidates = await DrillableQuestions(certId)
            .Where(q => q.SectionId == sectionId)
            .Select(q => new
            {
                q.QuestionId,
                q.CategoryId,
                History = db.UserQuestionHistories
                    .Where(h => h.UserId == userId && h.QuestionId == q.QuestionId)
                    .Select(h => new { h.LastAnsweredAt, h.CooldownUntil })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        // Projected as an anonymous type first and narrowed here: a nullable DateTimeOffset selected
        // through FirstOrDefault comes back as default(DateTimeOffset) in SQL, not null, which would
        // read every unseen question as "answered in year 1".
        var plan = candidates
            .Select(c => new PracticeCandidate(
                c.QuestionId,
                c.CategoryId,
                c.History?.LastAnsweredAt,
                c.History?.CooldownUntil))
            .ToList();

        var mastery = await LoadMasteryAsync(userId, cancellationToken);
        return SmartPracticePlanner.Plan(plan, mastery, now);
    }

    public async Task<PracticeSessionInfo> GetSessionInfoAsync(int userId, int sessionId, CancellationToken cancellationToken = default)
    {
        var session = await db.PracticeSessions
            .AsNoTracking()
            .Include(s => s.Section)
            .Include(s => s.Topic)
            .Where(s => s.SessionId == sessionId && s.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PracticeException(PracticeException.SessionNotFound, $"Practice session {sessionId} was not found.");

        return ToSessionInfo(session);
    }

    private async Task PruneOldSessionsAsync(int userId, int certId, CancellationToken cancellationToken)
    {
        var sessions = await db.PracticeSessions
            .Where(s => s.UserId == userId && s.CertId == certId && s.LearnerPathStepId == null)
            .OrderByDescending(s => s.StartedAt)
            .ThenByDescending(s => s.SessionId)
            .Select(s => s.SessionId)
            .ToListAsync(cancellationToken);

        if (sessions.Count <= MaxSavedSessionsPerCertificate)
        {
            return;
        }

        var excessSessionIds = sessions.Skip(MaxSavedSessionsPerCertificate).ToList();
        if (excessSessionIds.Count == 0)
        {
            return;
        }

        var insightIds = await db.AiInsights
            .Where(ai => ai.PracticeSessionId.HasValue && excessSessionIds.Contains(ai.PracticeSessionId.Value))
            .Select(ai => ai.InsightId)
            .ToListAsync(cancellationToken);

        var recos = await db.Recommendations
            .Where(r => (r.PracticeSessionId.HasValue && excessSessionIds.Contains(r.PracticeSessionId.Value))
                     || (r.InsightId.HasValue && insightIds.Contains(r.InsightId.Value)))
            .ToListAsync(cancellationToken);
        if (recos.Count > 0)
        {
            db.Recommendations.RemoveRange(recos);
        }

        var insights = await db.AiInsights
            .Where(ai => ai.PracticeSessionId.HasValue && excessSessionIds.Contains(ai.PracticeSessionId.Value))
            .ToListAsync(cancellationToken);
        if (insights.Count > 0)
        {
            db.AiInsights.RemoveRange(insights);
        }

        var answers = await db.PracticeAnswers
            .Where(pa => excessSessionIds.Contains(pa.SessionId))
            .ToListAsync(cancellationToken);
        if (answers.Count > 0)
        {
            db.PracticeAnswers.RemoveRange(answers);
        }

        var sessionsToRemove = await db.PracticeSessions
            .Where(s => excessSessionIds.Contains(s.SessionId))
            .ToListAsync(cancellationToken);
        if (sessionsToRemove.Count > 0)
        {
            db.PracticeSessions.RemoveRange(sessionsToRemove);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
