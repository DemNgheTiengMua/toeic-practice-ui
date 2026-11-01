using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>#155: a lesson's micro-check used to be graded by the browser against a
/// <c>data-correct</c> attribute, so the answer never reached the mastery machine and a certificate
/// could be walked through without a single recorded answer. Grading now happens here, server-side,
/// and writes the same mastery row a drill answer does.</summary>
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

        if (card.CardType != "micro_check" || card.CorrectOption is null)
        {
            throw new PracticeException(
                PracticeException.InvalidOption,
                $"Card {cardId} is not a micro-check with a gradeable answer.");
        }

        var selected = selectedOption.Trim().ToUpperInvariant();
        var isCorrect = string.Equals(selected, card.CorrectOption, StringComparison.Ordinal);

        // A lesson carries its category already. A lesson authored without one has nothing to record
        // mastery against, so the answer is graded and returned but no mastery row is invented.
        if (card.Lesson.CategoryId is { } categoryId)
        {
            await ApplyMasteryAsync(userId, categoryId, certId, isCorrect, _clock.GetUtcNow(), cancellationToken);
        }

        return new LessonMicroCheckAnswer(isCorrect, card.CorrectOption, card.Explanation, card.Lesson.CategoryId);
    }
}
