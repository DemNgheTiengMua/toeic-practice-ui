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

        var groups = await db.QuestionGroups.AsNoTracking()
            .Include(g => g.Section)
            .Include(g => g.QuestionGroups)
                .ThenInclude(q => q.QuestionOptionQuestionNavigations)
            .Include(g => g.QuestionGroups)
                .ThenInclude(q => q.Category)
            .Where(g => g.ExamId == examId)
            .OrderBy(g => g.DisplayOrder)
            // Two collection levels (questions, then options): split, or EF warns 20504 and the
            // rows multiply (see the readiness audit). Children are re-ordered in memory below.
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        // #269: an answer names a roster position, never a legacy question, so no row can cite one of
        // these questions. The set stays empty and the per-question flag reads false, which is the fact.
        var answeredQuestionIds = new HashSet<int>();

        var groupDtos = groups.Select(g => new AdminQuestionGroupDto(
            g.GroupId,
            g.ExamId,
            g.CertId,
            g.SectionId,
            g.Section.Code,
            g.Section.Name,
            EnumCodes.Parse<ModalityDto>(g.Section.Modality),
            g.Passage,
            g.AudioPath,
            g.AudioStartMs,
            g.AudioEndMs,
            g.ImagePath,
            g.ImageAlt,
            g.Transcript,
            g.PrepSeconds,
            g.ResponseSeconds,
            g.DisplayOrder,
            g.QuestionGroups.OrderBy(q => q.DisplayOrder).Select(q => new AdminQuestionDto(
                q.QuestionId,
                q.GroupId,
                q.ExamId,
                q.SectionId,
                g.Section.Code,
                g.Section.Name,
                q.SkillId,
                g.Section.Code,
                q.Stem,
                EnumCodes.Parse<QuestionTypeDto>(q.QuestionType),
                q.Explanation,
                q.CategoryId,
                q.Category?.Name,
                q.PrepSeconds,
                q.ResponseSeconds,
                q.MinWords,
                q.MaxWords,
                q.Hint,
                q.DisplayOrder,
                q.UpdatedAt,
                q.QuestionOptionQuestionNavigations.OrderBy(o => o.Label).Select(o => new AdminQuestionOptionDto(
                    o.OptionId,
                    o.QuestionId,
                    o.Label,
                    o.Text,
                    o.IsCorrect,
                    o.Rationale)).ToList(),
                answeredQuestionIds.Contains(q.QuestionId),
                MaxSelections: q.MaxSelections,
                AllowNumber: q.AllowNumber,
                AcceptedAnswersJson: q.AcceptedAnswersJson,
                RequiredKeywords: q.RequiredKeywords,
                SampleAnswerOrImageDesc: q.SampleAnswerOrImageDesc,
                ReferenceKind: ParseReferenceKind(q.ReferenceKind),
                CefrLevelCode: q.CefrLevel?.Code)).ToList(),
            UnitNumber: g.UnitNumber
        )).ToList();

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

    public async Task<AdminQuestionDto> CreateQuestionAsync(int examId, CreateQuestionRequest request, CancellationToken cancellationToken = default)
    {
        var group = await db.QuestionGroups
            .Include(g => g.Exam)
            .Include(g => g.Section)
            .FirstOrDefaultAsync(g => g.GroupId == request.GroupId && g.ExamId == examId, cancellationToken);

        if (group == null)
        {
            throw new QuestionGroupNotFoundException(request.GroupId);
        }

        // #157: adding a question changes the section total a finished attempt was scored against.
        await RequireStructureEditableAsync(examId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(group.Section.AllowedComponentTypesJson))
        {
            try
            {
                var allowed = JsonSerializer.Deserialize<List<string>>(group.Section.AllowedComponentTypesJson);
                if (allowed != null && allowed.Count > 0 && !allowed.Contains(request.QuestionType.ToCode(), StringComparer.OrdinalIgnoreCase))
                {
                    throw new AdminExamException(ApiError.Codes.Validation, $"Question type '{request.QuestionType.ToCode()}' is not allowed in section '{group.Section.Name}'.");
                }
            }
            catch (JsonException) { }
        }

        ValidateQuestionInputs(request.Stem, request.CefrLevelCode, request.QuestionType, request.PrepSeconds, request.ResponseSeconds, request.MinWords, request.MaxWords, request.Hint, request.Options, request.MaxSelections, request.AllowNumber, request.AcceptedAnswersJson, request.RequiredKeywords);
        ValidateReference(request.QuestionType, request.SampleAnswerOrImageDesc, request.ReferenceKind);

        if (request.CategoryId.HasValue)
        {
            var catExists = await db.QuestionCategories.AnyAsync(c => c.CategoryId == request.CategoryId.Value && c.CertId == group.CertId, cancellationToken);
            if (!catExists)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "Question category not found for certificate.");
            }
        }

        var question = new Question
        {
            GroupId = group.GroupId,
            ExamId = group.ExamId,
            SectionId = group.SectionId,
            SkillId = group.Section.SkillId,
            Stem = request.Stem.Trim(),
            CefrLevel = ParseAuthoredLevel(request.CefrLevelCode),
            QuestionType = request.QuestionType.ToCode(),
            Explanation = Normalize(request.Explanation),
            CategoryId = request.CategoryId,
            PrepSeconds = request.PrepSeconds,
            ResponseSeconds = request.ResponseSeconds,
            MinWords = request.MinWords,
            MaxWords = request.MaxWords,
            MaxSelections = request.MaxSelections,
            AllowNumber = request.AllowNumber,
            AcceptedAnswersJson = Normalize(request.AcceptedAnswersJson),
            RequiredKeywords = Normalize(request.RequiredKeywords),
            Hint = Normalize(request.Hint),
            SampleAnswerOrImageDesc = Normalize(request.SampleAnswerOrImageDesc),
            ReferenceKind = request.ReferenceKind?.ToCode(),
            DisplayOrder = request.DisplayOrder,
            UpdatedAt = clock.GetUtcNow()
        };

        db.Questions.Add(question);
        await db.SaveChangesAsync(cancellationToken);

        var createdOptions = new List<QuestionOption>();
        if (request.QuestionType != QuestionTypeDto.FreeResponse && request.Options != null)
        {
            foreach (var opt in request.Options)
            {
                var option = new QuestionOption
                {
                    QuestionId = question.QuestionId,
                    QuestionType = question.QuestionType,
                    Label = opt.Label.Trim().ToUpperInvariant(),
                    Text = opt.Text.Trim(),
                    IsCorrect = opt.IsCorrect,
                    Rationale = Normalize(opt.Rationale)
                };
                db.QuestionOptions.Add(option);
                createdOptions.Add(option);
            }
            await db.SaveChangesAsync(cancellationToken);
        }

        if (request.QuestionType == QuestionTypeDto.FreeResponse && request.CriterionIds != null)
        {
            await SaveSectionCriteriaAsync(
                group.SectionId,
                group.Section.SkillId,
                group.CertId,
                request.CriterionIds,
                cancellationToken);
        }

        group.Exam.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        return new AdminQuestionDto(
            question.QuestionId,
            question.GroupId,
            question.ExamId,
            question.SectionId,
            group.Section.Code,
            group.Section.Name,
            question.SkillId,
            group.Section.Code,
            question.Stem,
            request.QuestionType,
            question.Explanation,
            question.CategoryId,
            null,
            question.PrepSeconds,
            question.ResponseSeconds,
            question.MinWords,
            question.MaxWords,
            question.Hint,
            question.DisplayOrder,
            question.UpdatedAt,
            createdOptions.Select(o => new AdminQuestionOptionDto(o.OptionId, o.QuestionId, o.Label, o.Text, o.IsCorrect, o.Rationale)).ToList(),
            HasAnswers: false,
            MaxSelections: question.MaxSelections,
            AllowNumber: question.AllowNumber,
            AcceptedAnswersJson: question.AcceptedAnswersJson,
            RequiredKeywords: question.RequiredKeywords,
            SampleAnswerOrImageDesc: question.SampleAnswerOrImageDesc,
            ReferenceKind: ParseReferenceKind(question.ReferenceKind),
            CefrLevelCode: question.CefrLevel?.Code);
    }

    public async Task<AdminQuestionDto> UpdateQuestionAsync(int questionId, UpdateQuestionRequest request, CancellationToken cancellationToken = default)
    {
        var question = await db.Questions
            .Include(q => q.Group)
                .ThenInclude(g => g.Section)
            .Include(q => q.QuestionOptionQuestionNavigations)
            .FirstOrDefaultAsync(q => q.QuestionId == questionId, cancellationToken);

        // #265: the editor addresses a POSITION. On a pool paper that position is a roster row and its
        // content is the pinned version, so an autosave writes the version rather than a Question row
        // that does not exist. The branch is decided by which table holds the id: a pool paper has no
        // legacy row at all, so the two shapes cannot both match.
        if (question == null
            && await db.ExamItems.AsNoTracking().AnyAsync(i => i.ExamItemId == questionId, cancellationToken))
        {
            return await UpdatePoolPositionAsync(questionId, request, cancellationToken);
        }

        if (question == null)
        {
            throw new QuestionNotFoundException(questionId);
        }

        if (!string.IsNullOrWhiteSpace(question.Group.Section.AllowedComponentTypesJson))
        {
            try
            {
                var allowed = JsonSerializer.Deserialize<List<string>>(question.Group.Section.AllowedComponentTypesJson);
                if (allowed != null && allowed.Count > 0 && !allowed.Contains(request.QuestionType.ToCode(), StringComparer.OrdinalIgnoreCase))
                {
                    throw new AdminExamException(ApiError.Codes.Validation, $"Question type '{request.QuestionType.ToCode()}' is not allowed in section '{question.Group.Section.Name}'.");
                }
            }
            catch (JsonException) { }
        }

        ValidateQuestionInputs(request.Stem, request.CefrLevelCode, request.QuestionType, request.PrepSeconds, request.ResponseSeconds, request.MinWords, request.MaxWords, request.Hint, request.Options, request.MaxSelections, request.AllowNumber, request.AcceptedAnswersJson, request.RequiredKeywords);
        ValidateReference(request.QuestionType, request.SampleAnswerOrImageDesc, request.ReferenceKind);

        // #269: no attempt answer can cite a legacy question id - the column is gone with the table - so
        // this question has no recorded answers and the type-change refusal cannot apply.
        const bool hasAnswers = false;

        if (hasAnswers && question.QuestionType != request.QuestionType.ToCode())
        {
            throw new AdminExamException(ApiError.Codes.QuestionHasAttemptAnswers, "Cannot change question type of a question with recorded attempt answers.");
        }

        // #157: on an exam with finished attempts, only cosmetic fields may change. Comparing the
        // incoming values against what is stored keeps the rule structural rather than blanket:
        // a stem or explanation edit passes, an answer-key or option-set edit does not.
        // RequireStructureEditableAsync is a no-op when nothing has been sat, so it is the only query.
        if (ChangesStructure(question, request))
        {
            await RequireStructureEditableAsync(question.ExamId, cancellationToken);
        }

        if (request.CategoryId.HasValue)
        {
            var catExists = await db.QuestionCategories.AnyAsync(c => c.CategoryId == request.CategoryId.Value && c.CertId == question.Group.CertId, cancellationToken);
            if (!catExists)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "Question category not found for certificate.");
            }
        }

        if (request.GroupId.HasValue && request.GroupId.Value != question.GroupId)
        {
            var newGroup = await db.QuestionGroups
                .Include(g => g.Section)
                .FirstOrDefaultAsync(g => g.GroupId == request.GroupId.Value && g.ExamId == question.ExamId, cancellationToken);

            if (newGroup == null)
            {
                throw new QuestionGroupNotFoundException(request.GroupId.Value);
            }

            if (newGroup.SectionId != question.SectionId)
            {
                if (hasAnswers)
                {
                    throw new AdminExamException(ApiError.Codes.QuestionHasAttemptAnswers, "Cannot move a question with recorded answers to a different section.");
                }

                // #269: a practice answer names the item and version it answered, not a question, so the
                // practice-answer history of this legacy question cannot be read - and one cannot exist,
                // because the old rows are gone with the column.
                const bool hasPracticeAnswers = false;
                if (hasPracticeAnswers)
                {
                    throw new AdminExamException(ApiError.Codes.Validation, "Cannot move a question with recorded practice answers to a different section.");
                }
            }

            question.GroupId = newGroup.GroupId;
            question.SectionId = newGroup.SectionId;
            question.SkillId = newGroup.Section.SkillId;
        }

        question.Stem = request.Stem.Trim();
        // #229 / D1: the level replaces the difficulty, and a request that states none keeps the one
        // already stored. Clearing it would make an autosave of an unrelated field silently unlevel an
        // item, which is the same class of defect as defaulting it.
        if (!string.IsNullOrWhiteSpace(request.CefrLevelCode))
        {
            question.CefrLevel = ParseAuthoredLevel(request.CefrLevelCode);
        }

        question.QuestionType = request.QuestionType.ToCode();
        question.Explanation = Normalize(request.Explanation);
        question.CategoryId = request.CategoryId;
        question.PrepSeconds = request.PrepSeconds;
        question.ResponseSeconds = request.ResponseSeconds;
        question.MinWords = request.MinWords;
        question.MaxWords = request.MaxWords;
        question.MaxSelections = request.MaxSelections;
        question.AllowNumber = request.AllowNumber;
        question.AcceptedAnswersJson = Normalize(request.AcceptedAnswersJson);
        question.RequiredKeywords = Normalize(request.RequiredKeywords);
        question.Hint = Normalize(request.Hint);
        question.SampleAnswerOrImageDesc = Normalize(request.SampleAnswerOrImageDesc);
        question.ReferenceKind = request.ReferenceKind?.ToCode();
        if (request.DisplayOrder > 0)
        {
            question.DisplayOrder = request.DisplayOrder;
        }
        question.UpdatedAt = clock.GetUtcNow();

        if (request.QuestionType != QuestionTypeDto.FreeResponse && request.Options != null)
        {
            var incomingOptionLabels = request.Options.Select(o => o.Label.Trim().ToUpperInvariant()).ToHashSet();
            var optionsToRemove = question.QuestionOptionQuestionNavigations
                .Where(o => !incomingOptionLabels.Contains(o.Label))
                .ToList();

            foreach (var opt in optionsToRemove)
            {
                var optionAnswered = await db.AttemptAnswers.AnyAsync(a => a.SelectedOptionId == opt.OptionId, cancellationToken);
                if (optionAnswered)
                {
                    throw new AdminExamException(ApiError.Codes.QuestionHasAttemptAnswers, $"Cannot delete option {opt.Label} because it has recorded learner answers.");
                }

                var practiceAnswersWithOpt = await db.PracticeAnswers.Where(p => p.SelectedOptionId == opt.OptionId).ToListAsync(cancellationToken);
                foreach (var p in practiceAnswersWithOpt)
                {
                    p.SelectedOptionId = null;
                }

                db.QuestionOptions.Remove(opt);
            }

            // Save option removals & prevent UX_Options_OneCorrect duplicate key errors by clearing outgoing correct option first (for non multi_select)
            if (request.QuestionType != QuestionTypeDto.MultiSelect)
            {
                var currentlyCorrect = question.QuestionOptionQuestionNavigations.FirstOrDefault(o => o.IsCorrect);
                var incomingCorrect = request.Options.FirstOrDefault(o => o.IsCorrect);
                if (currentlyCorrect != null && incomingCorrect != null && !currentlyCorrect.Label.Equals(incomingCorrect.Label.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    currentlyCorrect.IsCorrect = false;
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            foreach (var optInput in request.Options)
            {
                var label = optInput.Label.Trim().ToUpperInvariant();
                var existingOpt = question.QuestionOptionQuestionNavigations.FirstOrDefault(o => o.Label == label);
                if (existingOpt != null)
                {
                    existingOpt.QuestionType = question.QuestionType;
                    existingOpt.Text = optInput.Text.Trim();
                    existingOpt.IsCorrect = optInput.IsCorrect;
                    existingOpt.Rationale = Normalize(optInput.Rationale);
                }
                else
                {
                    db.QuestionOptions.Add(new QuestionOption
                    {
                        QuestionId = question.QuestionId,
                        QuestionType = question.QuestionType,
                        Label = label,
                        Text = optInput.Text.Trim(),
                        IsCorrect = optInput.IsCorrect,
                        Rationale = Normalize(optInput.Rationale)
                    });
                }
            }
        }
        else if (request.QuestionType == QuestionTypeDto.FreeResponse)
        {
            if (question.QuestionOptionQuestionNavigations.Count > 0)
            {
                db.QuestionOptions.RemoveRange(question.QuestionOptionQuestionNavigations);
            }

            if (request.CriterionIds != null)
            {
                await SaveSectionCriteriaAsync(
                    question.SectionId,
                    question.SkillId,
                    question.Group.CertId,
                    request.CriterionIds,
                    cancellationToken);
            }
        }

        var exam = await db.Exams.FindAsync([question.ExamId], cancellationToken);
        if (exam != null)
        {
            exam.UpdatedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);

        var finalOptions = await db.QuestionOptions.AsNoTracking()
            .Where(o => o.QuestionId == questionId)
            .OrderBy(o => o.Label)
            .Select(o => new AdminQuestionOptionDto(o.OptionId, o.QuestionId, o.Label, o.Text, o.IsCorrect, o.Rationale))
            .ToListAsync(cancellationToken);

        return new AdminQuestionDto(
            question.QuestionId,
            question.GroupId,
            question.ExamId,
            question.SectionId,
            question.Group.Section.Code,
            question.Group.Section.Name,
            question.SkillId,
            question.Group.Section.Code,
            question.Stem,
            request.QuestionType,
            question.Explanation,
            question.CategoryId,
            null,
            question.PrepSeconds,
            question.ResponseSeconds,
            question.MinWords,
            question.MaxWords,
            question.Hint,
            question.DisplayOrder,
            question.UpdatedAt,
            finalOptions,
            hasAnswers,
            MaxSelections: question.MaxSelections,
            AllowNumber: question.AllowNumber,
            AcceptedAnswersJson: question.AcceptedAnswersJson,
            RequiredKeywords: question.RequiredKeywords,
            SampleAnswerOrImageDesc: question.SampleAnswerOrImageDesc,
            ReferenceKind: ParseReferenceKind(question.ReferenceKind),
            CefrLevelCode: question.CefrLevel?.Code);
    }

    public async Task DeleteQuestionAsync(int questionId, CancellationToken cancellationToken = default)
    {
        var question = await db.Questions
            .Include(q => q.QuestionOptionQuestionNavigations)
            .FirstOrDefaultAsync(q => q.QuestionId == questionId, cancellationToken);

        if (question == null)
        {
            throw new QuestionNotFoundException(questionId);
        }

        // #269: no attempt answer can cite a legacy question id - the column is gone with the table - so
        // this question has no recorded answers to protect.
        const bool hasAnswers = false;

        // The question-level refusal is more specific than the exam-wide lock, and it was the one
        // reported before #157, so it keeps precedence: an admin looking at one question wants to
        // hear about that question's answers first.
        if (hasAnswers)
        {
            throw new AdminExamException(ApiError.Codes.QuestionHasAttemptAnswers, "Cannot delete a question that has recorded learner answers in attempts.", 400);
        }

        // #157: deleting a question shrinks the section a finished attempt was scored on.
        await RequireStructureEditableAsync(question.ExamId, cancellationToken);

        // #269: practice answers name an item, not a question, so there is no per-question row to cascade.

        db.QuestionOptions.RemoveRange(question.QuestionOptionQuestionNavigations);
        db.Questions.Remove(question);

        var exam = await db.Exams.FindAsync([question.ExamId], cancellationToken);
        if (exam != null)
        {
            exam.UpdatedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);
    }

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

        // #157: reordering within a section is presentational, but an entry naming an item that does
        // not belong to this section is a move. Check before any write so nothing half-applies.
        var groupIds = request.Items.Where(i => i.GroupId.HasValue).Select(i => i.GroupId!.Value).ToList();
        var questionIds = request.Items.Where(i => i.QuestionId.HasValue).Select(i => i.QuestionId!.Value).ToList();

        var everyItemInSection =
            await db.QuestionGroups.AsNoTracking().CountAsync(g => groupIds.Contains(g.GroupId) && g.ExamId == examId && g.SectionId == request.SectionId, cancellationToken) == groupIds.Count
            && await db.Questions.AsNoTracking().CountAsync(q => questionIds.Contains(q.QuestionId) && q.ExamId == examId && q.SectionId == request.SectionId, cancellationToken) == questionIds.Count;

        if (!everyItemInSection)
        {
            await RequireStructureEditableAsync(examId, cancellationToken);
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var sorted = request.Items.OrderBy(i => i.DisplayOrder).ToList();
        var nextOrder = 1;

        foreach (var item in sorted)
        {
            if (item.GroupId.HasValue)
            {
                var grp = await db.QuestionGroups.FirstOrDefaultAsync(g => g.GroupId == item.GroupId.Value && g.ExamId == examId && g.SectionId == request.SectionId, cancellationToken);
                if (grp != null)
                {
                    grp.DisplayOrder = nextOrder;
                }
            }

            if (item.QuestionId.HasValue)
            {
                var q = await db.Questions.FirstOrDefaultAsync(x => x.QuestionId == item.QuestionId.Value && x.ExamId == examId && x.SectionId == request.SectionId, cancellationToken);
                if (q != null)
                {
                    q.DisplayOrder = nextOrder;
                }
            }

            nextOrder++;
        }

        exam.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}
