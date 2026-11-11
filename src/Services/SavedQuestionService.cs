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
/// #224: the saved bank, pool-native.
/// <para>
/// A link renders the version it pinned (<see cref="SavedQuestion.ItemVersionId"/>), never the item's
/// latest one. Its lifecycle is read separately so a withdrawn item still renders — flagged when it was
/// withdrawn as defective (D21).
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

        VersionHint = sq.ItemVersion != null ? sq.ItemVersion.Hint : null,
    };

    /// <summary>The projection's own shape. A class, not an anonymous type, so both reads share it.</summary>
    private sealed class SavedRow
    {
        public int SavedQuestionId { get; set; }
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
        public string? VersionHint { get; set; }
    }

    public async Task<SavedQuestionsView> GetSavedQuestionsAsync(int userId, SavedQuestionFilterInput filter, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);
        var cert = await db.Certificates
            .AsNoTracking()
            .Where(c => c.CertId == activeCertId)
            .Select(c => new CertificateOption(c.Code, c.Name, c.SectionNoun))
            .SingleAsync(cancellationToken);

        // Strict isolation: only this user's rows for this certificate. An item is certificate-agnostic
        // (D2), so the link carries the certificate itself.
        var baseQuery = db.SavedQuestions
            .AsNoTracking()
            .Where(sq => sq.UserId == userId && sq.CertId == activeCertId);

        var totalSaved = await baseQuery.CountAsync(cancellationToken);
        var manualSaved = await baseQuery.CountAsync(sq => sq.Source == SavedQuestionSource.Manual.ToCode(), cancellationToken);
        var incorrectSaved = await baseQuery.CountAsync(sq => sq.Source == SavedQuestionSource.Incorrect.ToCode(), cancellationToken);

        // #269: an item is section-free and category-free by design, so the bank has no per-section or
        // per-category facet left to build. The two lists stay in the view's shape and are empty.
        var sectionFilters = new List<SavedQuestionSectionFilter>();
        var categoryFilters = new List<SavedQuestionCategoryFilter>();

        var query = baseQuery;
        if (filter.Source is { } sourceFilter)
        {
            var sourceCode = sourceFilter.ToCode();
            query = query.Where(sq => sq.Source == sourceCode);
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
                && sq.CertId == activeCertId)
            .Select(RowProjection)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new SavedQuestionException(SavedQuestionException.SavedQuestionNotFound, $"Saved question {savedQuestionId} is not in your bank.");

        return ToItem(row);
    }

    /// <summary>
    /// Files a POSITION into the bank. The address is the <c>ExamItemId</c> the client was handed,
    /// exactly as every other answer-shape call in this codebase resolves it.
    /// <para>
    /// <b>#264: this used to be a legacy-only door.</b> It validated through the legacy question table
    /// and wrote a question id alone, so an item on a paper could not be filed at all. It resolves the
    /// position from the roster, takes the certificate scope from the BINDING (an item is
    /// certificate-agnostic by D2, so the item cannot supply it), and pins the version on screen.
    /// </para>
    /// </summary>
    public async Task<SaveQuestionResult> SaveQuestionAsync(int userId, int questionId, SavedQuestionSource source = SavedQuestionSource.Manual, CancellationToken cancellationToken = default)
    {
        var activeCertId = await learners.GetActiveCertIdAsync(userId, cancellationToken);

        // A position is a roster row. The scope lives on the binding, not the item.
        var position = await db.ExamItems
            .AsNoTracking()
            .Where(i => i.ExamItemId == questionId && i.CertId == activeCertId)
            .Select(i => new { i.ExamItemId, i.ItemId, i.ItemVersionId, i.CertId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new SavedQuestionException(SavedQuestionException.QuestionNotFound, $"Question {questionId} does not exist.");

        var alreadyFiled = await db.SavedQuestions
            .FirstOrDefaultAsync(sq => sq.UserId == userId && sq.ItemId == position.ItemId, cancellationToken);

        if (alreadyFiled is not null)
        {
            return await PromoteAsync(alreadyFiled, source, cancellationToken);
        }

        // A second save keeps the version already pinned; a first save pins the one on screen.
        var pinned = new SavedQuestion
        {
            UserId = userId,
            CertId = position.CertId,
            ItemId = position.ItemId,
            ItemVersionId = position.ItemVersionId,
            Source = source.ToCode(),
            SavedAt = DateTimeOffset.UtcNow,
        };

        db.SavedQuestions.Add(pinned);
        await db.SaveChangesAsync(cancellationToken);
        return new SaveQuestionResult(true, source, pinned.SavedAt, pinned.SavedQuestionId);
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

    /// <summary>
    /// Whether the item at this roster position is already in the learner's bank. The address is an
    /// <c>ExamItemId</c>, resolved to the item because a bookmark names the item, not a position.
    /// </summary>
    public async Task<bool> IsQuestionSavedAsync(int userId, int questionId, CancellationToken cancellationToken = default)
    {
        return await db.SavedQuestions
            .AnyAsync(sq => sq.UserId == userId
                && db.ExamItems.Any(i => i.ExamItemId == questionId && i.ItemId == sq.ItemId),
                cancellationToken);
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

    private static SavedQuestionItem ToItem(SavedRow row) =>
        new(
            SavedQuestionId: row.SavedQuestionId,
            QuestionId: null,
            ItemId: row.ItemId,
            ItemVersionId: row.ItemVersionId,
            IsRetiredDefective: string.Equals(row.LifecycleStatus, RetiredDefective, StringComparison.Ordinal),
            Source: EnumCodes.Parse<SavedQuestionSource>(row.Source),
            SavedAt: row.SavedAt,
            SectionId: null,
            SectionCode: null,
            SectionName: null,
            TopicCode: row.TopicCode,
            TopicName: row.TopicName,
            CategoryId: null,
            CategoryName: null,
            Stem: row.VersionStem ?? string.Empty,
            Passage: row.StimulusPassage,
            ImagePath: row.StimulusImagePath,
            AudioPath: row.StimulusAudioPath,
            Hint: row.VersionHint,
            Explanation: row.VersionExplanation,
            Transcript: row.StimulusTranscript,
            Options: PoolOptions(row.ItemVersionId!.Value, row.KeyLabel, row.OptionA, row.OptionB, row.OptionC, row.OptionD));

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
