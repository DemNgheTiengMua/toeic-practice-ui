using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AdminExamService
{
    /// <summary>
    /// The rationale for one option of a version. A version stores them one per column rather than as
    /// rows, so this is the only mapping in the editor.
    /// </summary>
    private static string? RationaleFor(ItemVersion version, string label) => label switch
    {
        "A" => version.RationaleA,
        "B" => version.RationaleB,
        "C" => version.RationaleC,
        "D" => version.RationaleD,
        _ => null,
    };

    /// <summary>
    /// #265: one autosave against a pool-authored paper position.
    /// <para>
    /// <b>Copy-on-write, not in-place (CNT-43).</b> A version's scored fields - the stem, the options,
    /// the key - are immutable by trigger, because an attempt pinned a version and re-scoring it after
    /// the fact would change what a finished sitting was graded on. So an edit that touches a scored
    /// field creates the NEXT version and points this binding at it; an edit to a non-scored field
    /// (explanation, hint, rationale) writes in place. The first draft wrote in place and the trigger
    /// refused it by name, which is the guard working.
    /// </para>
    /// <para>
    /// <b>What it may not change.</b> The item's level, topic and modality are the item's, not the
    /// paper's: changing them here would silently re-place an item every paper binds.
    /// </para>
    /// </summary>
    private async Task<AdminQuestionDto> UpdatePoolPositionAsync(
        int examItemId, UpdateQuestionRequest request, CancellationToken cancellationToken)
    {
        var row = await db.ExamItems
            .Include(i => i.Section)
                .ThenInclude(s => s.Skill)
            .FirstOrDefaultAsync(i => i.ExamItemId == examItemId, cancellationToken)
            ?? throw new QuestionNotFoundException(examItemId);

        var version = await db.ItemVersions
            .FirstOrDefaultAsync(v => v.ItemVersionId == row.ItemVersionId, cancellationToken)
            ?? throw new AdminExamException(
                ApiError.Codes.Validation,
                $"Position {examItemId} pins a version that no longer exists.");

        var item = await db.Items.FirstOrDefaultAsync(i => i.ItemId == row.ItemId, cancellationToken)
            ?? throw new AdminExamException(
                ApiError.Codes.Validation,
                $"Position {examItemId} names an item that no longer exists.");

        ValidateQuestionInputs(
            request.Stem, request.CefrLevelCode, request.QuestionType, request.PrepSeconds,
            request.ResponseSeconds, request.MinWords, request.MaxWords, request.Hint, request.Options,
            request.MaxSelections, request.AllowNumber, request.AcceptedAnswersJson, request.RequiredKeywords);
        ValidateReference(request.QuestionType, request.SampleAnswerOrImageDesc, request.ReferenceKind);

        var renderFormat = request.QuestionType.ToCode();
        var incomingKey = KeyLabelFor(request.Options, request.QuestionType);
        var isProductive = request.QuestionType == QuestionTypeDto.FreeResponse;

        var newStem = request.Stem.Trim();
        var newOptions = isProductive
            ? (A: (string?)null, B: (string?)null, C: (string?)null, D: (string?)null)
            : BuildOptionTexts(request.Options);

        // A scored field is the stem, any option text, or the key. Everything else can be written in
        // place; a scored change needs a new row (CNT-43).
        var scoredChange =
            !string.Equals(version.Stem, newStem, StringComparison.Ordinal)
            || !string.Equals(version.OptionA, newOptions.A, StringComparison.Ordinal)
            || !string.Equals(version.OptionB, newOptions.B, StringComparison.Ordinal)
            || !string.Equals(version.OptionC, newOptions.C, StringComparison.Ordinal)
            || !string.Equals(version.OptionD, newOptions.D, StringComparison.Ordinal)
            || !string.Equals(version.KeyLabel, incomingKey, StringComparison.Ordinal)
            || !string.Equals(item.RenderFormat, renderFormat, StringComparison.OrdinalIgnoreCase);

        if (scoredChange)
        {
            await RequireStructureEditableAsync(row.ExamId, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(request.CefrLevelCode))
        {
            var requested = ParseAuthoredLevel(request.CefrLevelCode);
            if (requested != item.CefrLevel)
            {
                throw new AdminExamException(
                    ApiError.Codes.Validation,
                    $"This position's item is authored at {item.CefrLevel.Code}; the editor cannot re-level an item "
                    + "from one paper, because every paper that binds it would move.");
            }
        }

        if (scoredChange)
        {
            // The next version number for THIS item, and a fresh row carrying the edited content. The
            // binding then moves to it, so what the editor shows and what the sitting renders agree.
            var nextNumber = await db.ItemVersions
                .Where(v => v.ItemId == item.ItemId)
                .MaxAsync(v => v.VersionNumber, cancellationToken) + 1;

            var next = new ItemVersion
            {
                ItemId = item.ItemId,
                VersionNumber = nextNumber,
                Stem = newStem,
                OptionA = newOptions.A,
                OptionB = newOptions.B,
                OptionC = newOptions.C,
                OptionD = newOptions.D,
                KeyLabel = incomingKey,
                Explanation = Normalize(request.Explanation),
                RationaleA = RationaleInputAt(request.Options, 0),
                RationaleB = RationaleInputAt(request.Options, 1),
                RationaleC = RationaleInputAt(request.Options, 2),
                RationaleD = RationaleInputAt(request.Options, 3),
                PrepSeconds = request.PrepSeconds,
                ResponseSeconds = request.ResponseSeconds,
                MinWords = request.MinWords,
                MaxWords = request.MaxWords,
                MaxSelections = request.MaxSelections,
                AllowNumber = request.AllowNumber,
                AcceptedAnswersJson = Normalize(request.AcceptedAnswersJson),
                RequiredKeywords = Normalize(request.RequiredKeywords),
                Hint = Normalize(request.Hint),
                ReferenceText = Normalize(request.SampleAnswerOrImageDesc),
                ReferenceKind = request.ReferenceKind?.ToCode(),
            };
            db.ItemVersions.Add(next);
            await db.SaveChangesAsync(cancellationToken);

            row.ItemVersionId = next.ItemVersionId;
            version = next;
        }
        else
        {
            // Nothing scored moved, so the same row can carry the descriptive fields.
            version.Explanation = Normalize(request.Explanation);
            version.RationaleA = RationaleInputAt(request.Options, 0);
            version.RationaleB = RationaleInputAt(request.Options, 1);
            version.RationaleC = RationaleInputAt(request.Options, 2);
            version.RationaleD = RationaleInputAt(request.Options, 3);
            version.Hint = Normalize(request.Hint);
            version.PrepSeconds = request.PrepSeconds;
            version.ResponseSeconds = request.ResponseSeconds;
            version.MinWords = request.MinWords;
            version.MaxWords = request.MaxWords;
            version.AcceptedAnswersJson = Normalize(request.AcceptedAnswersJson);
            version.RequiredKeywords = Normalize(request.RequiredKeywords);
            version.ReferenceText = Normalize(request.SampleAnswerOrImageDesc);
            version.ReferenceKind = request.ReferenceKind?.ToCode();
        }

        item.RenderFormat = renderFormat;

        if (request.DisplayOrder > 0)
        {
            row.DisplayOrder = request.DisplayOrder;
        }

        await db.SaveChangesAsync(cancellationToken);

        var options = AttemptEngine.IsProductive(version)
            ? []
            : AttemptEngine.OptionTexts(version)
                .Select((text, index) =>
                {
                    var label = AttemptEngine.OptionLabel(index);
                    return new AdminQuestionOptionDto(
                        AttemptEngine.PoolOptionId(version.ItemVersionId, index),
                        examItemId,
                        label,
                        text ?? string.Empty,
                        AttemptEngine.IsKeyLabel(version, label),
                        RationaleFor(version, label));
                })
                .ToList();

        var hasAnswers = await db.AttemptAnswers.AnyAsync(a => a.ExamItemId == examItemId, cancellationToken)
            || await db.FreeResponses.AnyAsync(r => r.ExamItemId == examItemId, cancellationToken);

        return new AdminQuestionDto(
            examItemId,
            item.StimulusId ?? -examItemId,
            row.ExamId,
            row.SectionId,
            row.Section.Code,
            row.Section.Name,
            row.Section.SkillId,
            row.Section.Skill?.Code ?? row.Section.Code,
            version.Stem,
            request.QuestionType,
            version.Explanation,
            CategoryId: null,
            CategoryName: item.Topic?.Name,
            PrepSeconds: version.PrepSeconds,
            ResponseSeconds: version.ResponseSeconds,
            MinWords: version.MinWords,
            MaxWords: version.MaxWords,
            Hint: version.Hint,
            DisplayOrder: row.DisplayOrder,
            UpdatedAt: DateTimeOffset.UtcNow,
            Options: options,
            HasAnswers: hasAnswers,
            MaxSelections: version.MaxSelections,
            AllowNumber: version.AllowNumber,
            AcceptedAnswersJson: version.AcceptedAnswersJson,
            RequiredKeywords: version.RequiredKeywords,
            SampleAnswerOrImageDesc: version.ReferenceText,
            ReferenceKind: version.ReferenceKind is null ? null : EnumCodes.Parse<ReferenceKindDto>(version.ReferenceKind),
            CefrLevelCode: item.CefrLevel.Code);
    }

    /// <summary>The four option texts, in label order, from an editor payload.</summary>
    private static (string? A, string? B, string? C, string? D) BuildOptionTexts(
        IReadOnlyList<AdminQuestionOptionInput>? options)
    {
        var ordered = (options ?? [])
            .OrderBy(o => o.Label.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(o => Normalize(o.Text))
            .ToList();

        return (
            ordered.ElementAtOrDefault(0),
            ordered.ElementAtOrDefault(1),
            ordered.ElementAtOrDefault(2),
            ordered.ElementAtOrDefault(3));
    }

    /// <summary>The rationale for the option at <paramref name="index"/> of a label-ordered payload.</summary>
    private static string? RationaleInputAt(IReadOnlyList<AdminQuestionOptionInput>? options, int index) =>
        (options ?? [])
            .OrderBy(o => o.Label.Trim(), StringComparer.OrdinalIgnoreCase)
            .ElementAtOrDefault(index)?.Rationale is { } r
                ? Normalize(r)
                : null;

    /// <summary>
    /// The key for an option set, in the canonical form <c>CK_ItemVersions_KeyLabel</c> accepts:
    /// comma-separated, ascending, no spaces. Multi-select keeps every correct label; a single-answer
    /// type keeps the first, because the CHECK would refuse two.
    /// </summary>
    private static string? KeyLabelFor(IReadOnlyList<AdminQuestionOptionInput>? options, QuestionTypeDto type)
    {
        if (options is null || options.Count == 0)
        {
            return null;
        }

        var correct = options
            .Where(o => o.IsCorrect)
            .Select(o => o.Label.Trim().ToUpperInvariant())
            .Where(l => l.Length == 1 && l[0] >= 'A' && l[0] <= 'D')
            .Distinct()
            .OrderBy(l => l, StringComparer.Ordinal)
            .ToList();

        if (correct.Count == 0)
        {
            return null;
        }

        return type == QuestionTypeDto.MultiSelect
            ? string.Join(',', correct)
            : correct[0];
    }

    public async Task<AdminExamQuestionsDto> GetExamQuestionsAsync(int examId, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.AsNoTracking()
            .Include(e => e.Cert)
            .FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);

        if (exam == null)
        {
            throw new ExamNotFoundException(examId);
        }

        // #266: a paper's questions are read from the model that HOLDS them. The importer writes the pool
        // and nothing else now, so a legacy-only read returns nothing for a freshly imported pack - which
        // is what the replace-mode test caught. Deciding by the roster's presence is the same rule the
        // sitting and the Studio use, so all three agree about what a paper contains.
        var roster = await db.ExamItems.AsNoTracking()
            .Where(i => i.ExamId == examId)
            .OrderBy(i => i.DisplayOrder)
            .ToListAsync(cancellationToken);

        if (roster.Count > 0)
        {
            return await GetPoolExamQuestionsAsync(exam, roster, cancellationToken);
        }

        // #269: no roster means no positions, and a position is the only thing that renders. The legacy
        // fallback that read QuestionGroups/Questions here is gone with those tables; a paper with
        // neither a roster nor legacy rows has nothing to show, which is what the editor says.
        var noRosterSections = await db.Sections.AsNoTracking()
            .Where(s => s.CertId == exam.CertId)
            .OrderBy(s => s.DisplayOrder)
            .Select(s => new AdminSectionOptionDto(
                s.SectionId, s.Code, s.Name, EnumCodes.Parse<ModalityDto>(s.Modality),
                s.ExpectedQuestionCount, s.DisplayOrder))
            .ToListAsync(cancellationToken);

        var noRosterCategories = await db.QuestionCategories.AsNoTracking()
            .Where(c => c.CertId == exam.CertId)
            .OrderBy(c => c.Code)
            .Select(c => new AdminQuestionCategoryOptionDto(c.CategoryId, c.Code, c.Name))
            .ToListAsync(cancellationToken);

        return new AdminExamQuestionsDto(
            exam.ExamId, exam.Name, exam.CertId, exam.Cert.Code,
            [], noRosterSections, noRosterCategories);
    }


    /// <summary>
    /// #266: a paper's questions read from the roster, in the shape the admin reader returns.
    /// <para>
    /// One group per stimulus, because that is the bundle the editor draws - and a binding's position is
    /// the paper's own, so the order is ascending by it rather than by any display-order copy.
    /// </para>
    /// </summary>
    private async Task<AdminExamQuestionsDto> GetPoolExamQuestionsAsync(
        Exam exam, List<ExamItem> roster, CancellationToken cancellationToken)
    {
        var positionIds = roster.Select(i => i.ExamItemId).ToList();

        var answeredIds = new HashSet<int>(await db.AttemptAnswers.AsNoTracking()
            .Where(a => positionIds.Contains(a.ExamItemId))
            .Select(a => a.ExamItemId)
            .Concat(db.FreeResponses.AsNoTracking()
                .Where(r => positionIds.Contains(r.ExamItemId))
                .Select(r => r.ExamItemId))
            .ToListAsync(cancellationToken));

        var sitting = await new AttemptEngine(db).LoadSittingQuestionsAsync(exam.ExamId, cancellationToken);
        var byPosition = sitting
            .ToDictionary(p => p.ExamItemId);

        var versions = await db.ItemVersions.AsNoTracking()
            .Where(v => roster.Select(i => i.ItemVersionId).Contains(v.ItemVersionId))
            .ToDictionaryAsync(v => v.ItemVersionId, cancellationToken);

        var items = await db.Items.AsNoTracking()
            .Where(i => roster.Select(r => r.ItemId).Contains(i.ItemId))
            .Include(i => i.Topic)
            .ToDictionaryAsync(i => i.ItemId, cancellationToken);

        var sections = await db.Sections.AsNoTracking()
            .Where(s => roster.Select(r => r.SectionId).Contains(s.SectionId))
            .Include(s => s.Skill)
            .ToDictionaryAsync(s => s.SectionId, cancellationToken);

        var groupDtos = roster
            .GroupBy(i => i.ItemId)
            .Select(g => g.OrderBy(i => i.DisplayOrder).First())
            .OrderBy(i => i.DisplayOrder)
            .Select(head =>
            {
                var section = sections[head.SectionId];
                var item = items[head.ItemId];
                var stimulus = head.ItemId != 0 && item.StimulusId is int sid
                    ? db.Stimuli.AsNoTracking().FirstOrDefault(s => s.StimulusId == sid)
                    : null;

                var members = roster
                    .Where(i => i.ItemId == head.ItemId)
                    .OrderBy(i => i.DisplayOrder)
                    .Select(i =>
                    {
                        var version = versions[i.ItemVersionId];
                        var position = byPosition.GetValueOrDefault(i.ExamItemId);
                        return new AdminQuestionDto(
                            i.ExamItemId,
                            i.ItemId,
                            i.ExamId,
                            i.SectionId,
                            section.Code,
                            section.Name,
                            section.SkillId,
                            section.Skill?.Code ?? section.Code,
                            version.Stem,
                            EnumCodes.Parse<QuestionTypeDto>(item.RenderFormat),
                            version.Explanation,
                            CategoryId: null,
                            CategoryName: item.Topic?.Name,
                            PrepSeconds: version.PrepSeconds,
                            ResponseSeconds: version.ResponseSeconds,
                            MinWords: version.MinWords,
                            MaxWords: version.MaxWords,
                            Hint: version.Hint,
                            DisplayOrder: i.DisplayOrder,
                            UpdatedAt: null,
                            Options: AttemptEngine.IsProductive(version)
                                ? []
                                : AttemptEngine.OptionTexts(version)
                                    .Select((text, index) =>
                                    {
                                        var label = AttemptEngine.OptionLabel(index);
                                        return new AdminQuestionOptionDto(
                                            AttemptEngine.PoolOptionId(version.ItemVersionId, index),
                                            i.ExamItemId,
                                            label,
                                            text ?? string.Empty,
                                            AttemptEngine.IsKeyLabel(version, label),
                                            RationaleFor(version, label));
                                    })
                                    .ToList(),
                            HasAnswers: answeredIds.Contains(i.ExamItemId),
                            MaxSelections: version.MaxSelections,
                            AllowNumber: version.AllowNumber,
                            AcceptedAnswersJson: version.AcceptedAnswersJson,
                            RequiredKeywords: version.RequiredKeywords,
                            SampleAnswerOrImageDesc: version.ReferenceText,
                            ReferenceKind: version.ReferenceKind is null
                                ? null
                                : EnumCodes.Parse<ReferenceKindDto>(version.ReferenceKind),
                            CefrLevelCode: item.CefrLevel.Code);
                    })
                    .ToList();

                return new AdminQuestionGroupDto(
                    head.ItemId,
                    head.ExamId,
                    head.CertId,
                    head.SectionId,
                    section.Code,
                    section.Name,
                    EnumCodes.Parse<ModalityDto>(section.Modality),
                    stimulus?.Passage,
                    MediaUrlHelper.NormalizeMediaUrl(stimulus?.AudioPath),
                    stimulus?.AudioStartMs,
                    stimulus?.AudioEndMs,
                    MediaUrlHelper.NormalizeMediaUrl(stimulus?.ImagePath),
                    stimulus?.ImageAlt,
                    stimulus?.Transcript,
                    stimulus?.PrepSeconds,
                    stimulus?.ResponseSeconds,
                    head.DisplayOrder,
                    members,
                    UnitNumber: head.UnitNumber);
            })
            .ToList();

        var availableSections = await db.Sections.AsNoTracking()
            .Where(s => s.CertId == exam.CertId)
            .OrderBy(s => s.DisplayOrder)
            .Select(s => new AdminSectionOptionDto(
                s.SectionId,
                s.Code,
                s.Name,
                EnumCodes.Parse<ModalityDto>(s.Modality),
                s.ExpectedQuestionCount,
                s.DisplayOrder))
            .ToListAsync(cancellationToken);

        var availableCategories = await db.QuestionCategories.AsNoTracking()
            .Where(c => c.CertId == exam.CertId)
            .OrderBy(c => c.Code)
            .Select(c => new AdminQuestionCategoryOptionDto(c.CategoryId, c.Code, c.Name))
            .ToListAsync(cancellationToken);

        return new AdminExamQuestionsDto(
            exam.ExamId,
            exam.Name,
            exam.CertId,
            exam.Cert.Code,
            groupDtos,
            availableSections,
            availableCategories);
    }

    // #269: Create/Update/DeleteQuestionAsync are GONE with the Questions table. An item is created
    // and edited through IAdminItemService, and placed on a paper through BindItemAsync - which is what
    // the Studio and the import door already call. No route called the removed three (they went in
    // #265), so no screen lost a feature.

    /// <summary>
    /// #265: reorder a pool paper's section. A binding's position is its rank within the section, and
    /// <c>UQ_ExamItems_Exam_Section_Order</c> makes (exam, section, position) unique - so the ranks are
    /// assigned from a temporary offset first, or two swaps collide mid-flight (measured on the content
    /// migration: an in-place renumber raised Msg 2627).
    /// </summary>
    private async Task ReorderPoolSectionAsync(
        Exam exam, ReorderSectionItemsRequest request, IReadOnlyList<int> rosterIds, CancellationToken cancellationToken)
    {
        // #157: reordering within a section is presentational, but an entry naming a row of a DIFFERENT
        // section is a move. Check before any write so nothing half-applies.
        var named = request.Items
            .SelectMany(i => new[] { i.GroupId, i.QuestionId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

        var everyEntryInSection = named.All(rosterIds.Contains);
        if (!everyEntryInSection)
        {
            await RequireStructureEditableAsync(exam.ExamId, cancellationToken);
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var sorted = request.Items.OrderBy(i => i.DisplayOrder).ToList();

        // Rank 1..n in the order the editor sent. A group entry on a pool paper names the stimulus, and
        // moving a group moves every position in it, so the individuals are renumbered from the same
        // sequence the editor produced.
        //
        // The ranks are assigned from a POSITIVE offset instead of a negative one: CK_ExamItems_DisplayOrder
        // refuses a position below 1, so parking rows at -n is not available. The offset is above any
        // existing rank in this section, so no intermediate state can collide.
        var currentMax = await db.ExamItems
            .Where(i => i.ExamId == exam.ExamId && i.SectionId == request.SectionId)
            .MaxAsync(i => (int?)i.DisplayOrder, cancellationToken) ?? 0;
        var offset = currentMax + sorted.Count + 1;

        var order = new List<(int RosterId, int Rank)>();
        var nextOrder = 1;
        foreach (var item in sorted)
        {
            var id = item.QuestionId ?? item.GroupId;
            if (id is not int positionId || !rosterIds.Contains(positionId))
            {
                continue;
            }

            order.Add((positionId, nextOrder));
            nextOrder++;
        }

        // Pass one: park every moved row above the section's own range.
        foreach (var (rosterId, rank) in order)
        {
            var row = await db.ExamItems.FirstOrDefaultAsync(
                i => i.ExamItemId == rosterId && i.ExamId == exam.ExamId && i.SectionId == request.SectionId,
                cancellationToken);

            if (row != null)
            {
                row.DisplayOrder = offset + rank;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // Pass two: assign the real ranks, now that no row holds one of them.
        foreach (var (rosterId, rank) in order)
        {
            var row = await db.ExamItems.FirstAsync(
                i => i.ExamItemId == rosterId, cancellationToken);
            row.DisplayOrder = rank;
        }

        exam.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task ReorderSectionItemsAsync(int examId, ReorderSectionItemsRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null || request.Items == null || request.Items.Count == 0) return;

        var exam = await db.Exams.FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);
        if (exam == null) throw new ExamNotFoundException(examId);

        // #265: the editor's up/down buttons send the position ids it was given. On a pool paper those
        // are roster ids, and the order they express is the roster's own - so the write is to ExamItems,
        // not to a question or group row. Which shape a request is, is decided ONCE, by whether any
        // entry names a roster row: a paper is authored on one model or the other, never both.
        var entryIds = request.Items
            .SelectMany(i => new[] { i.GroupId, i.QuestionId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        var rosterIds = await db.ExamItems.AsNoTracking()
            .Where(i => i.ExamId == examId && i.SectionId == request.SectionId && entryIds.Contains(i.ExamItemId))
            .Select(i => i.ExamItemId)
            .ToListAsync(cancellationToken);

        if (rosterIds.Count > 0)
        {
            await ReorderPoolSectionAsync(exam, request, rosterIds, cancellationToken);
            return;
        }
        // #269: with no roster there is nothing to reorder. The legacy path that renumbered
        // QuestionGroups/Questions rows is gone with those tables; a paper is authored on one model, and
        // that model is the roster.
    }
}
