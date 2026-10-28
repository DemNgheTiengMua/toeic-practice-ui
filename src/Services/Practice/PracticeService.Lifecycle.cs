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

        if (EnumCodes.Parse<Modality>(section.Modality) is not (Modality.Listening or Modality.Reading))
        {
            throw new PracticeException(PracticeException.ModalityNotAllowed, "Free practice is only available for listening and reading sections.");
        }

        var query = db.Questions
            .AsNoTracking()
            .Where(q => q.SectionId == section.SectionId && q.QuestionType != QuestionType.FreeResponse.ToCode() && q.QuestionType != "writing" && q.QuestionType != "speaking" && (q.AcceptedAnswersJson != null || db.QuestionOptions.Any(o => o.QuestionId == q.QuestionId)));

        if (input.QuestionSource == QuestionSource.WrongBefore)
        {
            var wrongFromPractice = db.PracticeAnswers
                .Where(pa => pa.Session.UserId == userId && pa.SectionId == section.SectionId && pa.IsCorrect == false)
                .Select(pa => pa.QuestionId);
            var wrongFromAttempts = db.AttemptAnswers
                .Where(aa => aa.Attempt.UserId == userId && aa.Question.SectionId == section.SectionId && aa.IsCorrect == false)
                .Select(aa => aa.QuestionId);
            var wrongIds = wrongFromPractice.Union(wrongFromAttempts);
            query = query.Where(q => wrongIds.Contains(q.QuestionId));
        }
        else if (input.QuestionSource == QuestionSource.Unseen)
        {
            var seenFromPractice = db.PracticeAnswers
                .Where(pa => pa.Session.UserId == userId && pa.SectionId == section.SectionId)
                .Select(pa => pa.QuestionId);
            var seenFromAttempts = db.AttemptAnswers
                .Where(aa => aa.Attempt.UserId == userId && aa.Question.SectionId == section.SectionId)
                .Select(aa => aa.QuestionId);
            var seenIds = seenFromPractice.Union(seenFromAttempts);
            query = query.Where(q => !seenIds.Contains(q.QuestionId));
        }

        var candidateQuestionIds = await query
            .OrderBy(q => q.DisplayOrder)
            .ThenBy(q => q.QuestionId)
            .Select(q => q.QuestionId)
            .ToListAsync(cancellationToken);

        if (candidateQuestionIds.Count == 0)
        {
            throw new PracticeException(PracticeException.NoQuestionsAvailable, "No questions match the selected criteria for this section.");
        }

        var takeCount = Math.Min(candidateQuestionIds.Count, input.QuestionCount);
        var selectedIds = candidateQuestionIds.Take(takeCount).ToList();

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
            QuestionSource = input.QuestionSource.ToCode(),
            FeedbackMode = input.FeedbackMode.ToCode(),
            StartedAt = DateTimeOffset.UtcNow,
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
            EnumCodes.Parse<FeedbackMode>(session.FeedbackMode),
            session.StartedAt,
            session.FinishedAt);
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
            EnumCodes.Parse<FeedbackMode>(session.FeedbackMode),
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
