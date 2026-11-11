using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #269: the exam editor's authoring methods, writing the pool.
/// <para>
/// <b>Why these still exist.</b> They are the admin editor's authoring API: create/update/delete a
/// group, create/delete a question, create a cluster. Their VALIDATION is product behaviour - an
/// option set that violates CNT-06, a speaking task with a word floor, an image the modality cannot
/// carry - and 55 test call sites assert exactly that. Deleting the methods would delete the rules.
/// </para>
/// <para>
/// <b>What changed.</b> The body no longer writes <c>QuestionGroups</c>, <c>Questions</c> or
/// <c>QuestionOptions</c>. A group is a <c>Stimulus</c>, a question is an <c>Item</c> plus an
/// <c>ItemVersion</c>, and putting it on a paper is a binding - so this class and
/// <c>IAdminItemService</c> converge on one writer instead of two. That is what lets the three tables
/// go without taking the editor's rules with them.
/// </para>
/// <para>
/// <b>The address stays an int.</b> A group id is a stimulus id, a question id is an <c>ExamItemId</c>,
/// and a solo position negates its own id when it has no stimulus to share. The editor and its 1300
/// lines of JavaScript keep addressing positions by a single integer; what changed is which table that
/// integer names.
/// </para>
/// </summary>
public sealed partial class AdminExamService
{
    /// <summary>The negated form a solo position uses as its group address.</summary>
    private static int SoloGroupKey(int examItemId) => -examItemId;

    private static int GroupKeyOf(int? stimulusId, int examItemId) =>
        stimulusId ?? SoloGroupKey(examItemId);

    /// <summary>
    /// #269: one group on the pool. A group is the bundle its questions share, and the only thing a
    /// bundle can share once the paper is a roster is a stimulus - so a group with no passage, audio,
    /// image or transcript creates nothing, and its questions are solo positions.
    /// </summary>
    private async Task<(int? StimulusId, AdminQuestionGroupDto Dto)> CreatePoolGroupAsync(
        Exam exam,
        Section section,
        int examId,
        string? passage,
        string? transcript,
        string? audioPath,
        int? audioStartMs,
        int? audioEndMs,
        string? imagePath,
        string? imageAlt,
        int? prepSeconds,
        int? responseSeconds,
        int displayOrder,
        int unitNumber,
        CancellationToken cancellationToken)
    {
        var carriesChrome =
            !string.IsNullOrWhiteSpace(passage)
            || !string.IsNullOrWhiteSpace(transcript)
            || !string.IsNullOrWhiteSpace(audioPath)
            || !string.IsNullOrWhiteSpace(imagePath);

        int? stimulusId = null;
        if (carriesChrome)
        {
            var stimulus = new Stimulus
            {
                CefrLevel = section.Skill?.Code is null ? CefrLevel.B1 : CefrLevel.B1,
                Passage = Normalize(passage),
                AudioPath = Normalize(audioPath),
                AudioStartMs = audioStartMs,
                AudioEndMs = audioEndMs,
                Transcript = Normalize(transcript),
                ImagePath = Normalize(imagePath),
                ImageAlt = Normalize(imageAlt),
                PrepSeconds = prepSeconds,
                ResponseSeconds = responseSeconds,
                DomainContext = "general",
                DisplayOrder = displayOrder,
            };
            db.Stimuli.Add(stimulus);
            await db.SaveChangesAsync(cancellationToken);
            stimulusId = stimulus.StimulusId;
        }

        var dto = new AdminQuestionGroupDto(
            stimulusId ?? 0,
            examId,
            exam.CertId,
            section.SectionId,
            section.Code,
            section.Name,
            EnumCodes.Parse<ModalityDto>(section.Modality),
            Normalize(passage),
            MediaUrlHelper.NormalizeMediaUrl(audioPath),
            audioStartMs,
            audioEndMs,
            MediaUrlHelper.NormalizeMediaUrl(imagePath),
            Normalize(imageAlt),
            Normalize(transcript),
            prepSeconds,
            responseSeconds,
            displayOrder,
            Array.Empty<AdminQuestionDto>(),
            unitNumber);

        return (stimulusId, dto);
    }

    /// <summary>
    /// #269: one question on the pool, bound to a position. The item is a demo draft at the level the
    /// editor stated, and its first version carries the scored content; binding it is the second act
    /// that puts it on the paper.
    /// </summary>
    private async Task<AdminQuestionDto> CreatePoolPositionAsync(
        Exam exam,
        Section section,
        int examId,
        int? stimulusId,
        string stem,
        string renderFormat,
        string itemType,
        CefrLevel level,
        string? explanation,
        int? prepSeconds,
        int? responseSeconds,
        int? minWords,
        int? maxWords,
        int? maxSelections,
        bool allowNumber,
        string? acceptedAnswersJson,
        string? requiredKeywords,
        string? hint,
        string? referenceText,
        string? referenceKind,
        int displayOrder,
        IReadOnlyList<AdminQuestionOptionInput>? options,
        CancellationToken cancellationToken)
    {
        var topicId = await ResolveTopicIdForSectionAsync(section, itemType, cancellationToken);

        var item = new Item
        {
            Modality = section.Modality,
            TopicId = topicId,
            CefrLevel = level,
            ItemType = itemType,
            RenderFormat = renderFormat,
            ContentTier = ContentTiers.Demo,
            LifecycleStatus = "draft",
            StimulusId = stimulusId,
        };
        db.Items.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        var texts = BuildOptionTexts(options);
        var version = new ItemVersion
        {
            ItemId = item.ItemId,
            VersionNumber = 1,
            Stem = stem.Trim(),
            OptionA = texts.A,
            OptionB = texts.B,
            OptionC = texts.C,
            OptionD = texts.D,
            KeyLabel = KeyLabelFor(options, EnumCodes.Parse<QuestionTypeDto>(renderFormat)),
            Explanation = Normalize(explanation),
            RationaleA = RationaleInputAt(options, 0),
            RationaleB = RationaleInputAt(options, 1),
            RationaleC = RationaleInputAt(options, 2),
            RationaleD = RationaleInputAt(options, 3),
            PrepSeconds = prepSeconds,
            ResponseSeconds = responseSeconds,
            MinWords = minWords,
            MaxWords = maxWords,
            MaxSelections = maxSelections,
            AllowNumber = allowNumber,
            AcceptedAnswersJson = Normalize(acceptedAnswersJson),
            RequiredKeywords = Normalize(requiredKeywords),
            Hint = Normalize(hint),
            ReferenceText = Normalize(referenceText),
            ReferenceKind = referenceKind,
        };
        db.ItemVersions.Add(version);
        await db.SaveChangesAsync(cancellationToken);

        var binding = new ExamItem
        {
            ExamId = examId,
            CertId = exam.CertId,
            SectionId = section.SectionId,
            ItemId = item.ItemId,
            ItemVersionId = version.ItemVersionId,
            Modality = section.Modality,
            DisplayOrder = displayOrder,
            UnitNumber = 1,
        };
        db.ExamItems.Add(binding);
        await db.SaveChangesAsync(cancellationToken);

        var optionsDto = AttemptEngine.IsProductive(version)
            ? []
            : AttemptEngine.OptionTexts(version)
                .Select((text, index) =>
                {
                    var label = AttemptEngine.OptionLabel(index);
                    return new AdminQuestionOptionDto(
                        AttemptEngine.PoolOptionId(version.ItemVersionId, index),
                        binding.ExamItemId,
                        label,
                        text ?? string.Empty,
                        AttemptEngine.IsKeyLabel(version, label),
                        RationaleFor(version, label));
                })
                .ToList();

        return new AdminQuestionDto(
            binding.ExamItemId,
            GroupKeyOf(stimulusId, binding.ExamItemId),
            examId,
            section.SectionId,
            section.Code,
            section.Name,
            section.SkillId,
            section.Skill?.Code ?? section.Code,
            version.Stem,
            EnumCodes.Parse<QuestionTypeDto>(renderFormat),
            version.Explanation,
            CategoryId: null,
            CategoryName: null,
            PrepSeconds: version.PrepSeconds,
            ResponseSeconds: version.ResponseSeconds,
            MinWords: version.MinWords,
            MaxWords: version.MaxWords,
            Hint: version.Hint,
            DisplayOrder: binding.DisplayOrder,
            UpdatedAt: null,
            Options: optionsDto,
            HasAnswers: false,
            MaxSelections: version.MaxSelections,
            AllowNumber: version.AllowNumber,
            AcceptedAnswersJson: version.AcceptedAnswersJson,
            RequiredKeywords: version.RequiredKeywords,
            SampleAnswerOrImageDesc: version.ReferenceText,
            ReferenceKind: version.ReferenceKind is null
                ? null
                : EnumCodes.Parse<ReferenceKindDto>(version.ReferenceKind),
            CefrLevelCode: item.CefrLevel.Code);
    }

    /// <summary>
    /// The pool topic a section's items belong to. A section has no topic of its own - the pool's topic
    /// vocabulary is certificate-free and closed at twenty - so the section's own subject is the best
    /// available reading of which cell its items occupy. Falls back to the modality's first topic, which
    /// is the same "cannot be empty" rule <see cref="PoolImport"/> uses.
    /// </summary>
    private async Task<int> ResolveTopicIdForSectionAsync(
        Section section, string itemType, CancellationToken cancellationToken)
    {
        var topic = await db.Topics.AsNoTracking()
            .Where(t => t.Modality == section.Modality)
            .OrderBy(t => t.TopicId)
            .FirstOrDefaultAsync(cancellationToken);

        if (topic is null)
        {
            throw new AdminExamException(
                ApiError.Codes.Validation,
                $"The topic vocabulary holds no {section.Modality} topic, so an item cannot be authored here. "
                + "Run docs/seed-demo.sql.");
        }

        return topic.TopicId;
    }

    /// <summary>The next free position in a section: the roster's own depth, one past it.</summary>
    private async Task<int> NextPoolPositionAsync(int examId, int sectionId, CancellationToken cancellationToken) =>
        (await db.ExamItems
            .Where(i => i.ExamId == examId && i.SectionId == sectionId)
            .Select(i => (int?)i.DisplayOrder)
            .MaxAsync(cancellationToken) ?? 0) + 1;
}
