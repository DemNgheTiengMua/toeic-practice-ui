using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>#155 / #240: a lesson's micro-check. It used to be graded by the browser against a
/// <c>data-correct</c> attribute, so the answer never reached the mastery machine and a certificate
/// could be walked through without a single recorded answer.
/// <para>
/// Grading is server-side, and the evidence is an ordinary <see cref="PracticeAnswer"/> row in an
/// ordinary lesson <see cref="PracticeSession"/>: the same ledger a drill answer writes into, which is
/// what makes <c>vw_UserTopicAccuracy</c> and the cell mastery the path sizes its steps from both
/// see it. A private counter in <c>LearnerLessonMicroChecks</c> would satisfy the ticket on paper and
/// be invisible to both.
/// </para></summary>
public sealed partial class PracticeService
{
    public async Task<LessonMicroCheckAnswer> SubmitLessonMicroCheckAsync(
        int userId, int lessonId, int cardId, string selectedOption, CancellationToken cancellationToken = default)
    {
        var certId = await learners.GetActiveCertIdAsync(userId, cancellationToken);

        var card = await db.LessonCards
            .AsNoTracking()
            .Include(c => c.Lesson)
            .Where(c => c.CardId == cardId && c.LessonId == lessonId && c.CertId == certId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PracticeException(
                PracticeException.QuestionNotFound,
                $"Micro-check card {cardId} was not found in lesson {lessonId}.");

        if (card.CardType != CardType.MicroCheck.ToCode() || card.CorrectOption is null)
        {
            throw new PracticeException(
                PracticeException.InvalidOption,
                $"Card {cardId} is not a micro-check with a gradeable answer.");
        }

        var selected = selectedOption.Trim().ToUpperInvariant();
        var isCorrect = string.Equals(selected, card.CorrectOption, StringComparison.Ordinal);

        // #159 review, restated for the ledger: a card is graded once PER LEARNER, not once per
        // sitting. Re-posting the same (now known) answer used to move mastery again every time, so
        // one card answered in a loop farmed a category to Mastered - the "click until green" #155
        // closed in the drill. The guard is this table because it is the one keyed by (learner, card);
        // #373: the stored row is the mastery guard, not the answer to this post - the reply reports
        // the verdict of the attempt just made, and the card still counts as reached.
        var alreadyGraded = await db.LearnerLessonMicroChecks
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.CardId == cardId, cancellationToken);

        if (alreadyGraded is not null)
        {
            await LessonProgressRecorder.RecordAsync(db, userId, lessonId, cardId, _clock.GetUtcNow(), cardDone: true);
            return new LessonMicroCheckAnswer(
                isCorrect, card.CorrectOption, card.Explanation, card.Lesson.CategoryId, AlreadyGraded: true);
        }

        PracticeSession? session = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            session = await LoadOrStartLessonSittingAsync(userId, certId, card.Lesson, cancellationToken);
            if (session is not null)
            {
                break;
            }

            var gradedWhileWaiting = await db.LearnerLessonMicroChecks
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.UserId == userId && m.CardId == cardId, cancellationToken);
            if (gradedWhileWaiting is not null)
            {
                await LessonProgressRecorder.RecordAsync(db, userId, lessonId, cardId, _clock.GetUtcNow(), cardDone: true);
                return new LessonMicroCheckAnswer(
                    isCorrect, card.CorrectOption, card.Explanation, card.Lesson.CategoryId, AlreadyGraded: true);
            }
        }

        if (session is null)
        {
            throw new InvalidOperationException(
                $"Could not load or start a lesson sitting for user {userId} and lesson {lessonId}.");
        }

        var now = _clock.GetUtcNow();
        var answer = new PracticeAnswer
        {
            SessionId = session.SessionId,
            CardId = cardId,
            SelectedLabel = selected,
            IsCorrect = isCorrect,
            HintUsed = false,
            DisplayOrder = await NextDisplayOrderAsync(session.SessionId, cancellationToken),
            AnsweredAt = now.UtcDateTime,
        };
        var microCheck = new LearnerLessonMicroCheck
        {
            UserId = userId,
            CardId = cardId,
            IsCorrect = isCorrect,
            AnsweredAt = now,
        };
        db.PracticeAnswers.Add(answer);
        db.LearnerLessonMicroChecks.Add(microCheck);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            db.Entry(answer).State = EntityState.Detached;
            db.Entry(microCheck).State = EntityState.Detached;

            var concurrent = await db.LearnerLessonMicroChecks
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.UserId == userId && m.CardId == cardId, cancellationToken);
            if (concurrent is not null)
            {
                await LessonProgressRecorder.RecordAsync(db, userId, lessonId, cardId, _clock.GetUtcNow(), cardDone: true);
                return new LessonMicroCheckAnswer(
                    isCorrect, card.CorrectOption, card.Explanation, card.Lesson.CategoryId, AlreadyGraded: true);
            }

            throw;
        }

        // #155: the same two writers a drill answer goes through, by category. #237 re-keys mastery
        // to the cell; until then the category axis is the one that exists, and a lesson without one
        // is graded but records no mastery row rather than inventing a category.
        if (card.Lesson.CategoryId is { } categoryId)
        {
            await ApplyMasteryAsync(userId, categoryId, certId, isCorrect, now, cancellationToken);
        }

        // #373: the card was reached by answering it, so it counts toward the module. Without this a
        // lesson sits at 3 of 4 cards forever and its path step never closes.
        await LessonProgressRecorder.RecordAsync(db, userId, lessonId, cardId, now, cardDone: true);

        await CloseLessonSittingIfFinishedAsync(session, cancellationToken);

        return new LessonMicroCheckAnswer(
            answer.IsCorrect == true, card.CorrectOption, card.Explanation, card.Lesson.CategoryId);
    }

    /// <summary>
    /// The learner's open sitting on this lesson, or a new one. A lesson sitting is an ordinary
    /// pool-shaped session: its cell is the lesson's own, so the answer it holds is attributable to
    /// (topic, level) exactly as a drill answer is.
    /// </summary>
    private async Task<PracticeSession?> LoadOrStartLessonSittingAsync(
        int userId, int certId, Lesson lesson, CancellationToken cancellationToken)
    {
        var open = await db.PracticeSessions
            .FirstOrDefaultAsync(s => s.UserId == userId
                && s.LessonId == lesson.LessonId
                && s.Status == PracticeStatus.InProgress.ToCode(), cancellationToken);

        if (open is not null)
        {
            return open;
        }

        var session = new PracticeSession
        {
            UserId = userId,
            CertId = certId,
            SectionId = null,
            TopicId = lesson.TopicId,
            CefrLevel = lesson.CefrLevel,
            Modality = lesson.Modality,
            LessonId = lesson.LessonId,
            QuestionCount = 1,
            Status = PracticeStatus.InProgress.ToCode(),
            CorrectCount = 0,
            QuestionSource = QuestionSource.All.ToCode(),
            StartedAt = _clock.GetUtcNow(),
        };

        db.PracticeSessions.Add(session);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return session;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            db.Entry(session).State = EntityState.Detached;
            return await db.PracticeSessions
                .FirstOrDefaultAsync(s => s.UserId == userId
                    && s.LessonId == lesson.LessonId
                    && s.Status == PracticeStatus.InProgress.ToCode(), cancellationToken);
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
        => ex.GetBaseException() is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };

    private async Task<int> NextDisplayOrderAsync(int sessionId, CancellationToken cancellationToken)
    {
        var highest = await db.PracticeAnswers
            .AsNoTracking()
            .Where(pa => pa.SessionId == sessionId)
            .MaxAsync(pa => (int?)pa.DisplayOrder, cancellationToken);
        return (highest ?? 0) + 1;
    }

    /// <summary>
    /// A lesson sitting finishes when every micro-check in the module it was opened for has been
    /// graded - which is the same rule a drill uses ("every row answered"). The sitting then records
    /// its manifest, so the summary and the ledger agree.
    /// </summary>
    private async Task CloseLessonSittingIfFinishedAsync(PracticeSession session, CancellationToken cancellationToken)
    {
        var module = await db.LessonCards
            .AsNoTracking()
            .Where(c => c.LessonId == session.LessonId && c.CardType == "micro_check")
            .Select(c => c.CardId)
            .ToListAsync(cancellationToken);

        if (module.Count == 0)
        {
            return;
        }

        var graded = await db.PracticeAnswers
            .AsNoTracking()
            .Where(pa => pa.SessionId == session.SessionId && pa.CardId != null)
            .Select(pa => pa.CardId!.Value)
            .CountAsync(cancellationToken);

        session.QuestionCount = module.Count;
        session.CorrectCount = await db.PracticeAnswers
            .AsNoTracking()
            .CountAsync(pa => pa.SessionId == session.SessionId && pa.IsCorrect == true, cancellationToken);

        if (graded >= module.Count)
        {
            session.Status = PracticeStatus.Finished.ToCode();
            session.FinishedAt = _clock.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
