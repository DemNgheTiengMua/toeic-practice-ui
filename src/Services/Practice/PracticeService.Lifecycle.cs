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
        var section = await db.Sections
            .AsNoTracking()
            .Where(s => s.SectionId == input.SectionId && s.CertId == activeCertId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PracticeException(PracticeException.SectionUnavailable, $"Section {input.SectionId} is not available for active certificate.");

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

        var selectedIds = await PlanSmartPracticeAsync(userId, activeCertId, section.SectionId, cancellationToken);

        if (selectedIds.Count == 0)
        {
            throw new PracticeException(PracticeException.NoQuestionsAvailable, "No questions match the selected criteria for this section.");
        }

        // Clean up or abandon any existing standalone in-progress sessions for this user and certificate
        var existingInProgress = await db.PracticeSessions
            .Where(s => s.UserId == userId && s.Section.CertId == activeCertId && s.Status == PracticeStatus.InProgress.ToCode() && s.LearnerPathStepId == null)
            .ToListAsync(cancellationToken);

        foreach (var inProg in existingInProgress)
        {
            var hasAnswers = await db.PracticeAnswers
                .AnyAsync(pa => pa.SessionId == inProg.SessionId && (pa.SelectedOptionId != null || pa.IsCorrect != null), cancellationToken);

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

        var session = new PracticeSession
        {
            UserId = userId,
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

        await PruneOldSessionsAsync(userId, activeCertId, cancellationToken);

        return new PracticeSessionInfo(
            session.SessionId,
            session.SectionId,
            section.Code,
            section.Name,
            EnumCodes.Parse<Modality>(session.Modality),
            session.QuestionCount,
            EnumCodes.Parse<PracticeStatus>(session.Status),
            session.CorrectCount,
            EnumCodes.Parse<QuestionSource>(session.QuestionSource),
            session.StartedAt,
            session.FinishedAt);
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
            .Where(s => s.SessionId == sessionId && s.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PracticeException(PracticeException.SessionNotFound, $"Practice session {sessionId} was not found.");

        return new PracticeSessionInfo(
            session.SessionId,
            session.SectionId,
            session.Section.Code,
            session.Section.Name,
            EnumCodes.Parse<Modality>(session.Modality),
            session.QuestionCount,
            EnumCodes.Parse<PracticeStatus>(session.Status),
            session.CorrectCount,
            EnumCodes.Parse<QuestionSource>(session.QuestionSource),
            session.StartedAt,
            session.FinishedAt);
    }

    private async Task PruneOldSessionsAsync(int userId, int certId, CancellationToken cancellationToken)
    {
        var sessions = await db.PracticeSessions
            .Where(s => s.UserId == userId && s.Section.CertId == certId && s.LearnerPathStepId == null)
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
