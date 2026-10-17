using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed class LessonService(CompanionDbContext db)
{
    public async Task<LessonDetailDto?> GetLessonAsync(int lessonId, int certId)
    {
        var lesson = await db.Lessons
            .Where(l => l.LessonId == lessonId && l.CertId == certId)
            .Select(l => new LessonDetailDto(
                l.LessonId,
                l.Title,
                l.Summary,
                l.SkillId,
                l.CategoryId,
                l.EstimatedMinutes,
                l.LessonCards
                    .OrderBy(c => c.DisplayOrder)
                    .Select(c => new LessonCardDto(
                        c.CardId,
                        c.CardType,
                        c.Title,
                        c.ContentMarkdown,
                        c.QuestionPrompt,
                        c.OptionA,
                        c.OptionB,
                        c.OptionC,
                        c.OptionD,
                        c.CorrectOption,
                        c.Explanation,
                        c.DisplayOrder
                    ))
                    .ToList()
            ))
            .SingleOrDefaultAsync();

        if (lesson is null)
            throw new InvalidOperationException($"Lesson {lessonId} not found in certificate {certId}");

        return lesson;
    }

    public async Task<LessonProgressDto> GetProgressAsync(int userId, int lessonId)
    {
        var totalCards = await db.LessonCards
            .Where(c => c.LessonId == lessonId)
            .CountAsync();

        var progress = await db.LearnerLessonProgresses
            .Where(p => p.UserId == userId && p.LessonId == lessonId)
            .Select(p => new LessonProgressDto(
                p.CardsCompleted,
                totalCards,
                p.IsCompleted,
                p.LastViewedCardId,
                p.FirstOpenedAt,
                p.CompletedAt,
                p.UpdatedAt
            ))
            .SingleOrDefaultAsync();

        return progress ?? new LessonProgressDto(0, totalCards, false, null, null, null, null);
    }

    public async Task RecordCardViewAsync(int userId, int lessonId, int cardId)
    {
        var progress = await db.LearnerLessonProgresses
            .Where(p => p.UserId == userId && p.LessonId == lessonId)
            .SingleOrDefaultAsync();

        var now = DateTimeOffset.UtcNow;

        var totalCards = await db.LessonCards
            .Where(c => c.LessonId == lessonId)
            .CountAsync();

        if (progress is null)
        {
            var lesson = await db.Lessons
                .Where(l => l.LessonId == lessonId)
                .Select(l => new { l.CertId })
                .SingleAsync();

            progress = new DataAccess.Scaffold.LearnerLessonProgress
            {
                UserId = userId,
                LessonId = lessonId,
                CertId = lesson.CertId,
                CardsCompleted = 1,
                TotalCards = totalCards,
                IsCompleted = false,
                LastViewedCardId = cardId,
                FirstOpenedAt = now,
                UpdatedAt = now
            };
            db.LearnerLessonProgresses.Add(progress);
        }
        else
        {
            // Only increment if this card is new (beyond the last viewed card in display order)
            var lastViewedOrder = await db.LessonCards
                .Where(c => c.CardId == progress.LastViewedCardId)
                .Select(c => c.DisplayOrder)
                .FirstOrDefaultAsync();

            var currentOrder = await db.LessonCards
                .Where(c => c.CardId == cardId)
                .Select(c => c.DisplayOrder)
                .FirstOrDefaultAsync();

            if (currentOrder > lastViewedOrder)
            {
                progress.CardsCompleted++;
            }

            progress.LastViewedCardId = cardId;
            progress.UpdatedAt = now;
        }

        if (progress.CardsCompleted >= totalCards)
        {
            progress.IsCompleted = true;
            progress.CompletedAt = now;
        }

        await db.SaveChangesAsync();
    }

    public async Task<LessonDetailDto?> GetLessonByCategoryAsync(int categoryId, int certId)
    {
        var lesson = await db.Lessons
            .Where(l => l.CategoryId == categoryId && l.CertId == certId)
            .OrderBy(l => l.DisplayOrder)
            .Select(l => new LessonDetailDto(
                l.LessonId,
                l.Title,
                l.Summary,
                l.SkillId,
                l.CategoryId,
                l.EstimatedMinutes,
                l.LessonCards
                    .OrderBy(c => c.DisplayOrder)
                    .Select(c => new LessonCardDto(
                        c.CardId,
                        c.CardType,
                        c.Title,
                        c.ContentMarkdown,
                        c.QuestionPrompt,
                        c.OptionA,
                        c.OptionB,
                        c.OptionC,
                        c.OptionD,
                        c.CorrectOption,
                        c.Explanation,
                        c.DisplayOrder
                    ))
                    .ToList()
            ))
            .FirstOrDefaultAsync();

        return lesson;
    }
}
