using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #240: the leveled lesson reader.
/// <para>
/// A lesson is addressed by (skill, topic, level) and carries the level itself, so "which lesson do I
/// read at R-DET B1" has one answer rather than "whichever lesson of the skill sorted first". It
/// carries two sub-modules - primary and remedial - as separate card sets, so a second attempt at the
/// same cell does not re-read identical instruction.
/// </para>
/// <para>
/// Progress is per learner per lesson <b>per module</b> and resumes at the exact card: the last card
/// viewed in that module, not card one. A lesson step closes only when the module the step opened is
/// complete, which is why the completion call names the module.
/// </para>
/// </summary>
public sealed class LessonService(CompanionDbContext db) : ILessonService
{
    /// <summary>
    /// The lesson addressed by one cell. The tie-break is DisplayOrder then LessonId so a cell holding
    /// more than one lesson answers reproducibly instead of returning whatever the engine picks.
    /// </summary>
    public async Task<LessonDetailDto?> GetLessonForCellAsync(
        int certId, int skillId, int topicId, string cefrLevel, string module)
    {
        var lessonId = await db.Lessons
            .Where(l => l.CertId == certId
                && l.SkillId == skillId
                && l.TopicId == topicId
                && l.CefrLevel == ParseAuthoredLevel(cefrLevel))
            .OrderBy(l => l.DisplayOrder)
            .ThenBy(l => l.LessonId)
            .Select(l => (int?)l.LessonId)
            .FirstOrDefaultAsync();

        return lessonId is { } id ? await GetLessonAsync(id, certId, module) : null;
    }

    /// <summary>
    /// One module of one lesson. <paramref name="module"/> names which; a module holding no cards
    /// falls back to the primary one, so a lesson authored before this ticket still opens.
    /// </summary>
    public async Task<LessonDetailDto?> GetLessonAsync(int lessonId, int certId, string module = LessonModule.Primary)
    {
        var lesson = await db.Lessons
            .AsNoTracking()
            .Include(l => l.Topic)
            .FirstOrDefaultAsync(l => l.LessonId == lessonId && l.CertId == certId);

        if (lesson is null)
        {
            throw new InvalidOperationException($"Lesson {lessonId} not found in certificate {certId}");
        }

        var cards = await LoadModuleCardsAsync(lessonId, module);
        var hasRemedial = await db.LessonCards
            .AnyAsync(c => c.LessonId == lessonId && c.Module == LessonModule.Remedial);

        // A remedial request for a lesson that has no remedial cards reads the primary module rather
        // than an empty page: an empty reader is indistinguishable from a broken one.
        var effectiveModule = cards.Count > 0 ? NormaliseModule(module) : LessonModule.Primary;
        if (cards.Count == 0)
        {
            cards = await LoadModuleCardsAsync(lessonId, LessonModule.Primary);
        }

        return new LessonDetailDto(
            lesson.LessonId,
            lesson.Title,
            lesson.Summary,
            lesson.SkillId,
            lesson.CategoryId,
            lesson.EstimatedMinutes,
            cards.Select(ToCardDto).ToList(),
            lesson.TopicId,
            lesson.Topic?.Code ?? "",
            lesson.Topic?.Name ?? "",
            lesson.CefrLevel.Code,
            effectiveModule,
            hasRemedial);
    }

    /// <summary>
    /// Where the learner stopped in one module. <see cref="LessonProgressDto.ResumeCardIndex"/> is the
    /// 1-based position of the last card viewed <b>in this module's own card list</b>, so a lesson whose
    /// primary module is finished does not drag the remedial module to its end.
    /// </summary>
    public async Task<LessonProgressDto> GetProgressAsync(
        int userId, int lessonId, string module = LessonModule.Primary)
    {
        var effectiveModule = NormaliseModule(module);
        var cardIds = await db.LessonCards
            .AsNoTracking()
            .Where(c => c.LessonId == lessonId && c.Module == effectiveModule)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.CardId)
            .Select(c => c.CardId)
            .ToListAsync();

        var progress = await db.LearnerLessonProgresses
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.LessonId == lessonId && p.Module == effectiveModule)
            .Select(p => new
            {
                p.CardsCompleted,
                p.IsCompleted,
                p.LastViewedCardId,
                p.FirstOpenedAt,
                p.CompletedAt,
                p.UpdatedAt,
            })
            .FirstOrDefaultAsync();

        var resumeIndex = 1;
        if (progress?.LastViewedCardId is { } lastCard)
        {
            var index = cardIds.IndexOf(lastCard);
            // A card that has since been deleted from the module leaves the reader at card one rather
            // than at a position that no longer exists.
            resumeIndex = index >= 0 ? index + 1 : 1;
        }

        return progress is null
            ? new LessonProgressDto(0, cardIds.Count, false, null, null, null, null, resumeIndex, effectiveModule)
            : new LessonProgressDto(
                progress.CardsCompleted,
                cardIds.Count,
                progress.IsCompleted,
                progress.LastViewedCardId,
                progress.FirstOpenedAt,
                progress.CompletedAt,
                progress.UpdatedAt,
                resumeIndex,
                effectiveModule);
    }

    /// <summary>
    /// Records that the learner viewed one card, in the module that card belongs to. The module is read
    /// off the card, never trusted from the caller - a client that claimed the wrong module would move
    /// progress the learner never made.
    /// </summary>
    public async Task RecordCardViewAsync(int userId, int lessonId, int cardId)
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
        var now = DateTimeOffset.UtcNow;

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
                CardsCompleted = 1,
                TotalCards = totalCards,
                LastViewedCardId = cardId,
                FirstOpenedAt = now,
                UpdatedAt = now,
            };
            db.LearnerLessonProgresses.Add(progress);
        }
        else
        {
            // Counted against the highest position already reached IN THIS MODULE, not against the
            // previously viewed card's order. Re-reading an earlier card then returning forward must
            // not double-count, and a jump to the last card completes the module once.
            var reachedIndex = progress.LastViewedCardId is { } previous
                ? moduleCards.IndexOf(previous)
                : -1;
            var currentIndex = moduleCards.IndexOf(cardId);

            if (currentIndex > reachedIndex)
            {
                progress.CardsCompleted = Math.Min(totalCards, progress.CardsCompleted + (currentIndex - reachedIndex));
            }

            progress.LastViewedCardId = cardId;
            progress.TotalCards = totalCards;
            progress.UpdatedAt = now;
        }

        if (progress.CardsCompleted >= totalCards)
        {
            progress.IsCompleted = true;
            progress.CompletedAt ??= now;
        }

        await db.SaveChangesAsync();

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
    }

    public async Task<LessonDetailDto?> GetLessonByCategoryAsync(int categoryId, int certId)
    {
        var lessonId = await db.Lessons
            .Where(l => l.CategoryId == categoryId && l.CertId == certId)
            .OrderBy(l => l.DisplayOrder)
            .ThenBy(l => l.LessonId)
            .Select(l => (int?)l.LessonId)
            .FirstOrDefaultAsync();

        return lessonId is { } id ? await GetLessonAsync(id, certId) : null;
    }

    /// <summary>The module the reader opens for a step type. An expanded remedial step reads the
    /// remedial module; every other lesson step reads the primary one.</summary>
    public static string ModuleForStepType(PathStepType stepType) =>
        stepType == PathStepType.ExpandedRemedial ? LessonModule.Remedial : LessonModule.Primary;

    private async Task<List<LessonCard>> LoadModuleCardsAsync(int lessonId, string module)
    {
        var effectiveModule = NormaliseModule(module);
        return await db.LessonCards
            .AsNoTracking()
            .Where(c => c.LessonId == lessonId && c.Module == effectiveModule)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.CardId)
            .ToListAsync();
    }

    private static LessonCardDto ToCardDto(LessonCard c) => new(
        c.CardId,
        c.CardType,
        c.Title,
        c.ContentMarkdown,
        c.QuestionPrompt,
        c.OptionA,
        c.OptionB,
        c.OptionC,
        c.OptionD,
        // #155: the key leaves the page. It is revealed by the graded reply instead, so the answer
        // reaches the mastery ledger and is not readable from the markup first.
        null,
        c.Explanation,
        c.DisplayOrder,
        c.Rationale,
        c.Module);

    /// <summary>An unknown module code is a caller bug, but the reader falls back to primary rather
    /// than showing nothing - the page must always render a lesson.</summary>
    private static string NormaliseModule(string? module) =>
        string.Equals(module, LessonModule.Remedial, StringComparison.OrdinalIgnoreCase)
            ? LessonModule.Remedial
            : LessonModule.Primary;

    private static CefrLevel ParseAuthoredLevel(string code) =>
        CefrLevel.TryParseAuthored(code, out var level)
            ? level
            : throw new ArgumentOutOfRangeException(nameof(code), code, "A lesson level must be A1..C2.");
}
