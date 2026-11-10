using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>#155: the mastery-owned half of practice. It is the only writer of
/// <c>UserCategoryMasteries</c> and <c>UserQuestionHistories</c>, so the cooldown and the state machine
/// cannot drift apart between the drill and the lesson micro-check - both come through here.</summary>
public sealed partial class PracticeService
{
    /// <summary>The mastery rows for one learner, keyed by category, as the pure machine reads them.</summary>
    private async Task<Dictionary<int, MasteryMachine.Snapshot>> LoadMasteryAsync(
        int userId, CancellationToken cancellationToken)
    {
        var rows = await db.UserCategoryMasteries
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(m => m.CategoryId, ToSnapshot);
    }

    private static MasteryMachine.Snapshot ToSnapshot(UserCategoryMastery row) => new(
        EnumCodes.Parse<MasteryState>(row.State),
        row.AnsweredCount,
        row.CorrectCount,
        row.Streak,
        row.ConsecutiveWrong,
        row.SessionDays,
        row.NextReviewDueAt,
        row.MasteredAt);

    /// <summary>
    /// Records one graded drill answer. <paramref name="isCorrect"/> null is a skip: the learner made no
    /// claim, so neither the question's cooldown nor its category's mastery moves.
    /// </summary>
    private async Task RecordAnswerAsync(
        int userId, Question question, int certId, bool? isCorrect, CancellationToken cancellationToken)
    {
        if (isCorrect is not { } correct)
        {
            return;
        }

        // #263: a POOL answer writes no per-question history - the cooldown for a pool item is derived
        // from the answer rows themselves, because #237 made the CELL the unit of mastery and the
        // selector already refuses this table as a source.
        //
        // shortcut: a legacy answer still writes it, because the legacy candidate loader (kept for a
        // section the pool has not stocked) still reads it. #269 deletes the table, the loader and this
        // line together; writing it for a pool answer is the part that had to stop now.
        if (question is not null)
        {
            var now = _clock.GetUtcNow();
            await RecordQuestionHistoryAsync(userId, question.QuestionId, correct, now, cancellationToken);

            if (question.CategoryId is { } categoryId)
            {
                await ApplyMasteryAsync(userId, categoryId, certId, correct, now, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Moves one category's mastery by one answer. The transition itself is
    /// <see cref="MasteryMachine.Apply"/>; everything here is persistence.
    /// </summary>
    private async Task<MasteryMachine.Snapshot> ApplyMasteryAsync(
        int userId, int categoryId, int certId, bool isCorrect, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var mastery = await db.UserCategoryMasteries
            .FirstOrDefaultAsync(m => m.UserId == userId && m.CategoryId == categoryId, cancellationToken);

        var current = mastery is null
            ? new MasteryMachine.Snapshot(MasteryState.Untested, 0, 0, 0, 0, 0, null, null)
            : ToSnapshot(mastery);

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var step = MasteryMachine.Apply(current, isCorrect, now, mastery?.LastSessionDay != today);

        if (mastery is null)
        {
            mastery = new UserCategoryMastery { UserId = userId, CategoryId = categoryId, CertId = certId };
            db.UserCategoryMasteries.Add(mastery);
        }

        mastery.State = step.Next.State.ToCode();
        mastery.AnsweredCount = step.Next.AnsweredCount;
        mastery.CorrectCount = step.Next.CorrectCount;
        mastery.Streak = step.Next.Streak;
        mastery.ConsecutiveWrong = step.Next.ConsecutiveWrong;
        mastery.SessionDays = step.Next.SessionDays;
        mastery.LastSessionDay = today;
        mastery.MasteredAt = step.Next.MasteredAt;
        mastery.LastAnsweredAt = now;
        mastery.NextReviewDueAt = step.NextReviewDueAt;
        mastery.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);
        return step.Next;
    }

    private async Task RecordQuestionHistoryAsync(
        int userId, int questionId, bool isCorrect, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var history = await db.UserQuestionHistories
            .FirstOrDefaultAsync(h => h.UserId == userId && h.QuestionId == questionId, cancellationToken);

        if (history is null)
        {
            history = new UserQuestionHistory { UserId = userId, QuestionId = questionId };
            db.UserQuestionHistories.Add(history);
        }

        history.LastAnsweredAt = now;
        history.LastIsCorrect = isCorrect;
        // A right answer clears the hold outright; a wrong one keeps the question out for a day. The
        // filter compares this against "now", so an expired cooldown needs no write to expire.
        history.CooldownUntil = isCorrect ? null : now + MasterySchedule.WrongAnswerCooldown;

        await db.SaveChangesAsync(cancellationToken);
    }
}
