using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #224: the saved bank, in its two shapes.
/// <para>
/// A pool link renders the version it pinned (<see cref="SavedQuestion.ItemVersionId"/>), never the
/// item's latest one, and never through the legacy <c>Questions</c> row an item does not have. Its
/// lifecycle is read separately so a withdrawn item still renders — flagged when it was withdrawn as
/// defective (D21).
/// </para>
/// </summary>
public sealed class SavedQuestionService(
    CompanionDbContext db,
    ILearnerService learners) : ISavedQuestionService
{
    private const string RetiredDefective = "retired_defective";

    /// <summary>
    /// One projection for both shapes, so the list and the single-item read cannot drift into
    /// rendering different things from the same row.
    /// <para>
    /// Every pool field is read from <c>ItemVersion</c> through the pin. Nothing here selects the
    /// item's latest version, which is the defect this ticket exists to make impossible.
    /// </para>
    /// </summary>
    private static readonly Expression<Func<SavedQuestion, SavedRow>> RowProjection = sq => new SavedRow
    {
        SavedQuestionId = sq.SavedQuestionId,
        QuestionId = sq.QuestionId,
        ItemId = sq.ItemId,
        ItemVersionId = sq.ItemVersionId,
        Source = sq.Source,
        SavedAt = sq.SavedAt,

        VersionStem = sq.ItemVersion != null ? sq.ItemVersion.Stem : null,
        VersionExplanation = sq.ItemVersion != null ? sq.ItemVersion.Explanation : null,
        OptionA = sq.ItemVersion != null ? sq.ItemVersion.OptionA : null,
        OptionB = sq.ItemVersion != null ? sq.ItemVersion.OptionB : null,
        OptionC = sq.ItemVersion != null ? sq.ItemVersion.OptionC : null,
        OptionD = sq.ItemVersion != null ? sq.ItemVersion.OptionD : null,
        KeyLabel = sq.ItemVersion != null ? sq.ItemVersion.KeyLabel : null,
        // The lifecycle now, not at save time: a link to an item withdrawn as defective has to say so
        // the next time it is opened.
        LifecycleStatus = sq.ItemVersion != null ? sq.ItemVersion.Item.LifecycleStatus : null,
        StimulusPassage = sq.ItemVersion != null && sq.ItemVersion.Item.Stimulus != null ? sq.ItemVersion.Item.Stimulus.Passage : null,
        StimulusAudioPath = sq.ItemVersion != null && sq.ItemVersion.Item.Stimulus != null ? sq.ItemVersion.Item.Stimulus.AudioPath : null,
        StimulusImagePath = sq.ItemVersion != null && sq.ItemVersion.Item.Stimulus != null ? sq.ItemVersion.Item.Stimulus.ImagePath : null,
        StimulusTranscript = sq.ItemVersion != null && sq.ItemVersion.Item.Stimulus != null ? sq.ItemVersion.Item.Stimulus.Transcript : null,
        TopicCode = sq.ItemVersion != null ? sq.ItemVersion.Item.Topic.Code : null,
        TopicName = sq.ItemVersion != null ? sq.ItemVersion.Item.Topic.Name : null,

        LegacySectionId = sq.Question != null ? sq.Question.SectionId : (int?)null,
        LegacySectionCode = sq.Question != null ? sq.Question.Section.Code : null,
        LegacySectionName = sq.Question != null ? sq.Question.Section.Name : null,
        LegacyCategoryId = sq.Question != null ? sq.Question.CategoryId : null,
        LegacyCategoryName = sq.Question != null && sq.Question.Category != null ? sq.Question.Category.Name : null,
        LegacyStem = sq.Question != null ? sq.Question.Stem : null,
        LegacyPassage = sq.Question != null && sq.Question.Group != null ? sq.Question.Group.Passage : null,
        LegacyImagePath = sq.Question != null && sq.Question.Group != null ? sq.Question.Group.ImagePath : null,
        LegacyAudioPath = sq.Question != null && sq.Question.Group != null ? sq.Question.Group.AudioPath : null,
        LegacyHint = sq.Question != null ? sq.Question.Hint : null,
        LegacyExplanation = sq.Question != null ? sq.Question.Explanation : null,
        LegacyTranscript = sq.Question != null && sq.Question.Group != null ? sq.Question.Group.Transcript : null,
        LegacyOptions = sq.Question != null
            ? sq.Question.QuestionOptionQuestionNavigations
                .OrderBy(o => o.Label)
                .Select(o => new LegacyOptionRow
                {
                    OptionId = o.OptionId,
                    Label = o.Label,
                    Text = o.Text,
                    IsCorrect = o.IsCorrect,
                })
                .ToList()
            : null,
    };

    /// <summary>The projection's own shape. A class, not an anonymous type, so both reads share it.</summary>
    private sealed class SavedRow
    {
        public int SavedQuestionId { get; set; }
        public int? QuestionId { get; set; }
        public int? ItemId { get; set; }
        public int? ItemVersionId { get; set; }
        public string Source { get; set; } = null!;
        public DateTimeOffset SavedAt { get; set; }

        public string? VersionStem { get; set; }
        public string? VersionExplanation { get; set; }
        public string? OptionA { get; set; }
        public string? OptionB { get; set; }
        public string? OptionC { get; set; }
        public string? OptionD { get; set; }
        public string? KeyLabel { get; set; }
        public string? LifecycleStatus { get; set; }
        public string? StimulusPassage { get; set; }
        public string? StimulusAudioPath { get; set; }
        public string? StimulusImagePath { get; set; }
        public string? StimulusTranscript { get; set; }
        public string? TopicCode { get; set; }
        public string? TopicName { get; set; }

        public int? LegacySectionId { get; set; }
        public string? LegacySectionCode { get; set; }
        public string? LegacySectionName { get; set; }
        public int? LegacyCategoryId { get; set; }
        public string? LegacyCategoryName { get; set; }
        public string? LegacyStem { get; set; }
        public string? LegacyPassage { get; set; }
        public string? LegacyImagePath { get; set; }
        public string? LegacyAudioPath { get; set; }
        public string? LegacyHint { get; set; }
        public string? LegacyExplanation { get; set; }
        public string? LegacyTranscript { get; set; }
        public List<LegacyOptionRow>? LegacyOptions { get; set; }
    }

    private sealed class LegacyOptionRow
    {
        public int OptionId { get; set; }
        public string Label { get; set; } = null!;
        public string Text { get; set; } = null!;
        public bool IsCorrect { get; set; }
    }

    public async Task<SavedQuestionsView> GetSavedQuestionsAsync(int userId, SavedQuestionFilterInput filter, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var cert = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == activeCertId)
            .Select(c => new CertificateOption(c.Code, c.Name, c.SectionNoun))
            .SingleAsync(cancellationToken);

        // Strict isolation: only this user's rows for this certificate. A legacy link is scoped through
        // its question's section; a pool link carries the certificate itself, because an item has none.
        var baseQuery = db.SavedQuestions
            .AsNoTracking()
            .Where(sq => sq.UserId == userId
                && (sq.Question != null ? sq.Question.Section.CertId == activeCertId : sq.CertId == activeCertId));

        var totalSaved = await baseQuery.CountAsync(cancellationToken);
        var manualSaved = await baseQuery.CountAsync(sq => sq.Source == SavedQuestionSource.Manual.ToCode(), cancellationToken);
        var incorrectSaved = await baseQuery.CountAsync(sq => sq.Source == SavedQuestionSource.Incorrect.ToCode(), cancellationToken);

        var sectionFilters = (await baseQuery
            .Where(sq => sq.QuestionId != null)
            .Select(sq => new
            {
                sq.Question!.SectionId,
                sq.Question.Section.Code,
                sq.Question.Section.Name,
                sq.Question.Section.DisplayOrder
            })
            .ToListAsync(cancellationToken))
            .GroupBy(s => new { s.SectionId, s.Code, s.Name, s.DisplayOrder })
            .OrderBy(g => g.Key.DisplayOrder)
            .ThenBy(g => g.Key.Code)
            .Select(g => new SavedQuestionSectionFilter(g.Key.SectionId, g.Key.Code, g.Key.Name, g.Count()))
            .ToList();

        var categoryFilters = (await baseQuery
            .Where(sq => sq.Question != null && sq.Question.CategoryId != null)
            .Select(sq => new { CategoryId = sq.Question!.CategoryId!.Value, Name = sq.Question.Category!.Name })
            .ToListAsync(cancellationToken))
            .GroupBy(c => new { c.CategoryId, c.Name })
            .OrderBy(g => g.Key.Name)
            .Select(g => new SavedQuestionCategoryFilter(g.Key.CategoryId, g.Key.Name, g.Count()))
            .ToList();

        var query = baseQuery;
        if (filter.Source is { } sourceFilter)
        {
            var sourceCode = sourceFilter.ToCode();
            query = query.Where(sq => sq.Source == sourceCode);
        }

        if (filter.SectionId.HasValue)
        {
            query = query.Where(sq => sq.Question != null && sq.Question.SectionId == filter.SectionId.Value);
        }

        if (filter.CategoryId.HasValue)
        {
            query = query.Where(sq => sq.Question != null && sq.Question.CategoryId == filter.CategoryId.Value);
        }

        var questions = (await query
            .OrderByDescending(sq => sq.SavedAt)
            .Select(RowProjection)
            .ToListAsync(cancellationToken))
            .Select(ToItem)
            .ToList();

        return new SavedQuestionsView(
            cert,
            totalSaved,
            manualSaved,
            incorrectSaved,
            questions,
            sectionFilters,
            categoryFilters);
    }

    public async Task<SavedQuestionItem> GetSavedQuestionAsync(int userId, int savedQuestionId, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var row = await db.SavedQuestions
            .AsNoTracking()
            .Where(sq => sq.UserId == userId && sq.SavedQuestionId == savedQuestionId
                && (sq.Question != null ? sq.Question.Section.CertId == activeCertId : sq.CertId == activeCertId))
            .Select(RowProjection)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new SavedQuestionException(SavedQuestionException.SavedQuestionNotFound, $"Saved question {savedQuestionId} is not in your bank.");

        return ToItem(row);
    }

    public async Task<SaveQuestionResult> SaveQuestionAsync(int userId, int questionId, SavedQuestionSource source = SavedQuestionSource.Manual, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var questionExists = await db.Questions
            .AnyAsync(q => q.QuestionId == questionId && q.Section.CertId == activeCertId, cancellationToken);
        if (!questionExists)
        {
            throw new SavedQuestionException(SavedQuestionException.QuestionNotFound, $"Question {questionId} does not exist.");
        }

        var existing = await db.SavedQuestions
            .FirstOrDefaultAsync(sq => sq.UserId == userId && sq.QuestionId == questionId, cancellationToken);

        if (existing != null)
        {
            return await PromoteAsync(existing, source, cancellationToken);
        }

        var saved = new SavedQuestion
        {
            UserId = userId,
            QuestionId = questionId,
            Source = source.ToCode(),
            SavedAt = DateTimeOffset.UtcNow
        };

        db.SavedQuestions.Add(saved);
        await db.SaveChangesAsync(cancellationToken);

        return new SaveQuestionResult(true, source, saved.SavedAt, saved.SavedQuestionId);
    }

    /// <summary>
    /// #224: files a pool item, pinning the version that was on screen.
    /// <para>
    /// A second save of the same item is the same bookmark: it keeps the version already pinned rather
    /// than moving the link to the item's latest version, which is exactly the behaviour this ticket
    /// exists to prevent.
    /// </para>
    /// </summary>
    public async Task<SaveQuestionResult> SaveItemVersionAsync(int userId, int itemVersionId, SavedQuestionSource source = SavedQuestionSource.Manual, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);

        var version = await db.ItemVersions
            .AsNoTracking()
            .Where(v => v.ItemVersionId == itemVersionId)
            .Select(v => new { v.ItemVersionId, v.ItemId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new SavedQuestionException(SavedQuestionException.QuestionNotFound, $"Item version {itemVersionId} does not exist.");

        var existing = await db.SavedQuestions
            .FirstOrDefaultAsync(sq => sq.UserId == userId && sq.ItemId == version.ItemId, cancellationToken);

        if (existing != null)
        {
            return await PromoteAsync(existing, source, cancellationToken);
        }

        var saved = new SavedQuestion
        {
            UserId = userId,
            CertId = activeCertId,
            ItemId = version.ItemId,
            ItemVersionId = version.ItemVersionId,
            Source = source.ToCode(),
            SavedAt = DateTimeOffset.UtcNow
        };

        db.SavedQuestions.Add(saved);
        await db.SaveChangesAsync(cancellationToken);

        return new SaveQuestionResult(true, source, saved.SavedAt, saved.SavedQuestionId);
    }

    public async Task<SaveQuestionResult> UnsaveAsync(int userId, int savedQuestionId, CancellationToken cancellationToken = default)
    {
        var existing = await db.SavedQuestions
            .FirstOrDefaultAsync(sq => sq.UserId == userId && sq.SavedQuestionId == savedQuestionId, cancellationToken);

        if (existing == null)
        {
            // Idempotent: unsaving something unsaved succeeds with IsSaved = false
            return new SaveQuestionResult(false, null, null);
        }

        db.SavedQuestions.Remove(existing);
        await db.SaveChangesAsync(cancellationToken);

        return new SaveQuestionResult(false, null, null);
    }

    public async Task<bool> IsQuestionSavedAsync(int userId, int questionId, CancellationToken cancellationToken = default)
    {
        return await db.SavedQuestions
            .AnyAsync(sq => sq.UserId == userId && sq.QuestionId == questionId, cancellationToken);
    }

    /// <summary>
    /// Idempotent, and one-way: a manual save upgrades an automatic one. The pinned version is never
    /// touched here — re-saving is not re-pinning.
    /// </summary>
    private async Task<SaveQuestionResult> PromoteAsync(SavedQuestion existing, SavedQuestionSource source, CancellationToken cancellationToken)
    {
        if (source == SavedQuestionSource.Manual && existing.Source == SavedQuestionSource.Incorrect.ToCode())
        {
            existing.Source = SavedQuestionSource.Manual.ToCode();
            await db.SaveChangesAsync(cancellationToken);
        }

        return new SaveQuestionResult(true, EnumCodes.Parse<SavedQuestionSource>(existing.Source), existing.SavedAt, existing.SavedQuestionId);
    }

    private static SavedQuestionItem ToItem(SavedRow row)
    {
        var isPool = row.ItemVersionId is not null;

        return new SavedQuestionItem(
            SavedQuestionId: row.SavedQuestionId,
            QuestionId: row.QuestionId,
            ItemId: row.ItemId,
            ItemVersionId: row.ItemVersionId,
            IsRetiredDefective: string.Equals(row.LifecycleStatus, RetiredDefective, StringComparison.Ordinal),
            Source: EnumCodes.Parse<SavedQuestionSource>(row.Source),
            SavedAt: row.SavedAt,
            SectionId: row.LegacySectionId,
            SectionCode: row.LegacySectionCode,
            SectionName: row.LegacySectionName,
            TopicCode: row.TopicCode,
            TopicName: row.TopicName,
            CategoryId: row.LegacyCategoryId,
            CategoryName: row.LegacyCategoryName,
            Stem: (isPool ? row.VersionStem : row.LegacyStem) ?? string.Empty,
            Passage: isPool ? row.StimulusPassage : row.LegacyPassage,
            ImagePath: isPool ? row.StimulusImagePath : row.LegacyImagePath,
            AudioPath: isPool ? row.StimulusAudioPath : row.LegacyAudioPath,
            Hint: row.LegacyHint,
            Explanation: isPool ? row.VersionExplanation : row.LegacyExplanation,
            Transcript: isPool ? row.StimulusTranscript : row.LegacyTranscript,
            Options: isPool
                ? PoolOptions(row.ItemVersionId!.Value, row.KeyLabel, row.OptionA, row.OptionB, row.OptionC, row.OptionD)
                : (row.LegacyOptions ?? [])
                    .Select(o => new SavedQuestionOption(o.OptionId, o.Label, o.Text, o.IsCorrect))
                    .ToList());
    }

    /// <summary>
    /// A version stores labels A-D rather than option rows, so the id is derived from
    /// (version, index) — the same bijection <see cref="AttemptEngine"/> uses. A productive version has
    /// no options at all, and yields none rather than four blank ones.
    /// </summary>
    private static IReadOnlyList<SavedQuestionOption> PoolOptions(
        int itemVersionId, string? keyLabel, string? optionA, string? optionB, string? optionC, string? optionD)
    {
        if (optionA is null)
        {
            return [];
        }

        var texts = new[] { optionA, optionB, optionC, optionD };
        var labels = new[] { "A", "B", "C", "D" };
        return texts
            .Select((text, index) => new SavedQuestionOption(
                AttemptEngine.PoolOptionId(itemVersionId, index),
                labels[index],
                text ?? string.Empty,
                string.Equals(labels[index], keyLabel, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
}
