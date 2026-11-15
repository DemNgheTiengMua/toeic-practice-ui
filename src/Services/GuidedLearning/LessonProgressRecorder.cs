using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #240 / #373: one writer for "the learner reached this card in this module". A card view and a
/// micro-check submission both advance the same row, so a lesson cannot stall at 3 of 4 cards because
/// one of the two surfaces never touched the counter.
/// <para>
/// Finishing the module closes its path step here, which is why both callers get the unlock without
/// repeating the closure rule.
/// </para>
/// </summary>
public static class LessonProgressRecorder
{
    /// <summary>
    /// Records that <paramref name="cardId"/> was reached and closes the lesson step when that
    /// finishes the module. A card must belong to <paramref name="lessonId"/>.
    /// </summary>
    /// <param name="cardDone">True when the caller knows the card is finished (an answered
    /// micro-check), not merely displayed. The position counter alone cannot see it: a learner who
    /// jumps to the last card and back leaves the high-water mark on that card while the count is
    /// short, so a later visit to it never increments.</param>
    public static async Task<LearnerLessonProgress> RecordAsync(
        CompanionDbContext db, int userId, int lessonId, int cardId, DateTimeOffset now, bool cardDone = false)
    {
        var card = await db.LessonCards
            .AsNoTracking()
            .Where(c => c.CardId == cardId && c.LessonId == lessonId)
            .Select(c => new { c.CardId, c.Module, c.DisplayOrder })
            .SingleOrDefaultAsync()
            ?? throw new InvalidOperationException($"Card {cardId} is not a card of lesson {lessonId}.");

        var lesson = await db.Lessons
            .AsNoTracking()
            .Where(l => l.LessonId == lessonId)
            .Select(l => new { l.CertId })
            .SingleAsync();

        var moduleCards = await db.LessonCards
            .AsNoTracking()
            .Where(c => c.LessonId == lessonId && c.Module == card.Module)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.CardId)
            .Select(c => c.CardId)
            .ToListAsync();

        var totalCards = moduleCards.Count;

        var progress = await db.LearnerLessonProgresses
            .FirstOrDefaultAsync(p => p.UserId == userId && p.LessonId == lessonId && p.Module == card.Module);

        if (progress is null)
        {
            progress = new LearnerLessonProgress
            {
                UserId = userId,
                LessonId = lessonId,
                CertId = lesson.CertId,
                Module = card.Module,
                // A finished card counts as far as its own position, so answering the last card
                // completes a module the learner never browsed.
                CardsCompleted = cardDone ? Math.Min(totalCards, moduleCards.IndexOf(cardId) + 1) : 1,
                TotalCards = totalCards,
                LastViewedCardId = cardId,
                FirstOpenedAt = now,
                UpdatedAt = now,
            };
            db.LearnerLessonProgresses.Add(progress);
            if (progress.CardsCompleted >= totalCards)
            {
                progress.IsCompleted = true;
                progress.CompletedAt ??= now;
            }

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.GetBaseException() is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
            {
                db.Entry(progress).State = EntityState.Detached;
                progress = await db.LearnerLessonProgresses
                    .FirstOrDefaultAsync(p => p.UserId == userId && p.LessonId == lessonId && p.Module == card.Module);
                if (progress is null)
                {
                    throw;
                }

                ApplyCardView(progress, moduleCards, cardId, totalCards, now, cardDone);
                await db.SaveChangesAsync();
            }
        }
        else
        {
            ApplyCardView(progress, moduleCards, cardId, totalCards, now, cardDone);
            await db.SaveChangesAsync();
        }

        // #154 / #240: finishing the module the step opened closes the step. A step that closed on the
        // first card view would mark a lesson done that the learner has not read.
        if (progress.IsCompleted)
        {
            await PathStepCompletion.CloseAsync(
                db,
                userId,
                PathStepCompletion.Activity.Lesson,
                lessonId,
                score: null,
                accuracyPct: null,
                lessonModule: progress.Module);
        }

        return progress;
    }

    private static void ApplyCardView(
        LearnerLessonProgress progress,
        List<int> moduleCards,
        int cardId,
        int totalCards,
        DateTimeOffset now,
        bool cardDone)
    {
        // Counted against the highest position already reached IN THIS MODULE, not against the
        // previously viewed card's order. Re-reading an earlier card then returning forward must
        // not double-count, and a jump to the last card completes the module once.
        var reachedIndex = progress.LastViewedCardId is { } previous
            ? moduleCards.IndexOf(previous)
            : -1;
        var currentIndex = moduleCards.IndexOf(cardId);

        // #373: an answered card is done even when it is the card the pointer already sits on. The
        // high-water mark cannot tell "re-reading the last card" from "finishing it".
        var target = cardDone
            ? Math.Max(progress.CardsCompleted, currentIndex + 1)
            : currentIndex > reachedIndex
                ? progress.CardsCompleted + (currentIndex - reachedIndex)
                : progress.CardsCompleted;

        progress.CardsCompleted = Math.Min(totalCards, target);
        progress.LastViewedCardId = cardId;
        progress.TotalCards = totalCards;
        progress.UpdatedAt = now;

        if (progress.CardsCompleted >= totalCards)
        {
            progress.IsCompleted = true;
            progress.CompletedAt ??= now;
        }
    }
}
