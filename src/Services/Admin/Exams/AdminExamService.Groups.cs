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

    public async Task<AdminQuestionGroupDto> CreateQuestionGroupAsync(int examId, CreateQuestionGroupRequest request, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);
        if (exam == null)
        {
            throw new ExamNotFoundException(examId);
        }

        // #157: a group brings questions with it, so it changes the section's shape.
        await RequireStructureEditableAsync(examId, cancellationToken);

        var section = await db.Sections.FirstOrDefaultAsync(s => s.SectionId == request.SectionId && s.CertId == exam.CertId, cancellationToken);
        if (section == null)
        {
            throw new AdminExamException(ApiError.Codes.Validation, $"Section with id {request.SectionId} not found for certificate {exam.CertId}.");
        }

        ValidateQuestionGroup(exam.Purpose, section.Modality, request.ImagePath, request.ImageAlt, request.AudioStartMs, request.AudioEndMs, request.PrepSeconds, request.ResponseSeconds);

        var group = new QuestionGroup
        {
            ExamId = examId,
            CertId = exam.CertId,
            SectionId = request.SectionId,
            Passage = Normalize(request.Passage),
            AudioPath = Normalize(request.AudioPath),
            AudioStartMs = request.AudioStartMs,
            AudioEndMs = request.AudioEndMs,
            ImagePath = Normalize(request.ImagePath),
            ImageAlt = Normalize(request.ImageAlt),
            Transcript = Normalize(request.Transcript),
            PrepSeconds = request.PrepSeconds,
            ResponseSeconds = request.ResponseSeconds,
            DisplayOrder = request.DisplayOrder,
            UnitNumber = request.UnitNumber > 0 ? request.UnitNumber : 1
        };

        db.QuestionGroups.Add(group);
        exam.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        return new AdminQuestionGroupDto(
            group.GroupId,
            group.ExamId,
            group.CertId,
            group.SectionId,
            section.Code,
            section.Name,
            EnumCodes.Parse<ModalityDto>(section.Modality),
            group.Passage,
            group.AudioPath,
            group.AudioStartMs,
            group.AudioEndMs,
            group.ImagePath,
            group.ImageAlt,
            group.Transcript,
            group.PrepSeconds,
            group.ResponseSeconds,
            group.DisplayOrder,
            Array.Empty<AdminQuestionDto>(),
            group.UnitNumber);
    }

    public async Task<AdminQuestionGroupDto> UpdateQuestionGroupAsync(int examId, int groupId, UpdateQuestionGroupRequest request, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);
        if (exam == null)
        {
            throw new ExamNotFoundException(examId);
        }

        var group = await db.QuestionGroups
            .FirstOrDefaultAsync(g => g.GroupId == groupId && g.ExamId == examId, cancellationToken);

        if (group == null)
        {
            throw new QuestionGroupNotFoundException(groupId);
        }

        var section = await db.Sections.FirstOrDefaultAsync(s => s.SectionId == request.SectionId && s.CertId == exam.CertId, cancellationToken);
        if (section == null)
        {
            throw new AdminExamException(ApiError.Codes.Validation, $"Section with id {request.SectionId} not found for certificate {exam.CertId}.");
        }

        // #157: moving a group between sections changes which section a finished attempt was scored
        // over. Content edits inside the same section stay allowed.
        if (group.SectionId != request.SectionId)
        {
            await RequireStructureEditableAsync(examId, cancellationToken);
        }

        ValidateQuestionGroup(exam.Purpose, section.Modality, request.ImagePath, request.ImageAlt, request.AudioStartMs, request.AudioEndMs, request.PrepSeconds, request.ResponseSeconds);

        group.SectionId = request.SectionId;
        group.Passage = Normalize(request.Passage);
        group.AudioPath = Normalize(request.AudioPath);
        group.AudioStartMs = request.AudioStartMs;
        group.AudioEndMs = request.AudioEndMs;
        group.ImagePath = Normalize(request.ImagePath);
        group.ImageAlt = Normalize(request.ImageAlt);
        group.Transcript = Normalize(request.Transcript);
        group.PrepSeconds = request.PrepSeconds;
        group.ResponseSeconds = request.ResponseSeconds;
        if (request.DisplayOrder > 0)
        {
            group.DisplayOrder = request.DisplayOrder;
        }
        group.UnitNumber = request.UnitNumber > 0 ? request.UnitNumber : 1;

        exam.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        return new AdminQuestionGroupDto(
            group.GroupId,
            group.ExamId,
            group.CertId,
            group.SectionId,
            section.Code,
            section.Name,
            EnumCodes.Parse<ModalityDto>(section.Modality),
            group.Passage,
            group.AudioPath,
            group.AudioStartMs,
            group.AudioEndMs,
            group.ImagePath,
            group.ImageAlt,
            group.Transcript,
            group.PrepSeconds,
            group.ResponseSeconds,
            group.DisplayOrder,
            Array.Empty<AdminQuestionDto>(),
            group.UnitNumber);
    }

    public async Task DeleteQuestionGroupAsync(int examId, int groupId, CancellationToken cancellationToken = default)
    {
        var group = await db.QuestionGroups
            .Include(g => g.QuestionGroups)
                .ThenInclude(q => q.QuestionOptionQuestionNavigations)
            .FirstOrDefaultAsync(g => g.GroupId == groupId && g.ExamId == examId, cancellationToken);

        if (group == null)
        {
            throw new QuestionGroupNotFoundException(groupId);
        }

        // #157: deleting the group removes its questions, shrinking a section a finished attempt was
        // scored over. The per-question answer check below only sees the questions that have answers.
        await RequireStructureEditableAsync(examId, cancellationToken);

        // #269: no attempt answer or free response can cite a legacy question id - the column is gone with
        // the table - so no question in this group has a recorded answer, and the refusal that guarded
        // against deleting an answered one has nothing left to test.

        foreach (var q in group.QuestionGroups)
        {
            // #269: practice answers name an item, not a question, so there is no per-question row to cascade.

            db.QuestionOptions.RemoveRange(q.QuestionOptionQuestionNavigations);
            db.Questions.Remove(q);
        }

        db.QuestionGroups.Remove(group);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// #269: a cluster - one bundle plus its children - authored on the pool.
    /// <para>
    /// The bundle is a <c>Stimulus</c> when it carries a passage, audio or an image, and nothing when it
    /// does not; the children are items bound to consecutive positions. The stems, the default options
    /// and the free-response rules are unchanged - they are the editor's behaviour, and every one of
    /// them is asserted by an existing test.
    /// </para>
    /// </summary>
    public async Task<AdminQuestionGroupDto> CreateExamClusterAsync(int examId, CreateExamClusterRequest request, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);
        if (exam == null) throw new ExamNotFoundException(examId);

        // #157: a cluster adds a group plus its questions in one call.
        await RequireStructureEditableAsync(examId, cancellationToken);

        var section = await db.Sections.Include(s => s.Skill).FirstOrDefaultAsync(s => s.SectionId == request.SectionId && s.CertId == exam.CertId, cancellationToken);
        if (section == null)
        {
            throw new AdminExamException(ApiError.Codes.Validation, $"Section with id {request.SectionId} not found for certificate {exam.CertId}.");
        }

        var isFreeResponse = section.Modality is "speaking" or "writing"
            || (section.AllowedComponentTypesJson != null && section.AllowedComponentTypesJson.Contains("free_response") && !section.AllowedComponentTypesJson.Contains("mcq"));

        var firstPosition = await NextPoolPositionAsync(examId, request.SectionId, cancellationToken);

        var (stimulusId, groupDto) = await CreatePoolGroupAsync(
            exam, section, examId,
            request.Passage, request.Transcript, request.AudioPath,
            request.AudioStartMs, request.AudioEndMs,
            imagePath: null, imageAlt: null,
            prepSeconds: null, responseSeconds: null,
            displayOrder: firstPosition,
            unitNumber: request.UnitNumber > 0 ? request.UnitNumber : 1,
            cancellationToken);

        var count = request.QuestionCount > 0 ? Math.Clamp(request.QuestionCount, 1, 10) : (section.ClusterSize > 0 ? section.ClusterSize : 3);
        // #229 / D1: a cluster is content, so it states its level once and every child is authored at
        // it. There is no default: the previous fixture wrote difficulty 2 for every cluster, which is
        // exactly the silent choice this ticket removes.
        var level = ParseAuthoredLevel(request.CefrLevelCode);

        var questionDtos = new List<AdminQuestionDto>();
        for (var i = 0; i < count; i++)
        {
            string stem;
            if (section.RequireImage)
            {
                stem = "Look at the photograph and choose the statement that best describes what you see in the image.";
            }
            else if (section.HideOptionText)
            {
                stem = "Mark your answer on your answer sheet.";
            }
            else if (isFreeResponse)
            {
                stem = "Respond to the prompt as instructed.";
            }
            else
            {
                stem = $"Question {firstPosition + i}";
            }

            var qType = isFreeResponse ? QuestionTypeDto.FreeResponse
                : (section.OptionCount == 3 ? QuestionTypeDto.Mcq_3 : QuestionTypeDto.Mcq);

            var optionLabels = section.OptionCount == 3 ? new[] { "A", "B", "C" } : new[] { "A", "B", "C", "D" };
            var defaultOptions = isFreeResponse
                ? null
                : optionLabels
                    .Select(lbl => new AdminQuestionOptionInput(
                        null, lbl, $"Option {lbl}",
                        lbl == "A",
                        lbl == "A" ? "Correct response" : "Distractor response"))
                    .ToList();

            questionDtos.Add(await CreatePoolPositionAsync(
                exam, section, examId, stimulusId,
                stem: stem,
                renderFormat: qType.ToCode(),
                itemType: ItemTypeFor(section, isFreeResponse),
                level: level,
                explanation: section.HideOptionText ? "Option A is the most appropriate response." : "Correct answer option A.",
                prepSeconds: isFreeResponse ? (section.Modality == "speaking" ? 15 : 60) : null,
                responseSeconds: isFreeResponse ? (section.Modality == "speaking" ? 45 : 1800) : null,
                minWords: section.Modality == "writing" ? 150 : null,
                maxWords: section.Modality == "writing" ? 300 : null,
                maxSelections: null,
                allowNumber: true,
                acceptedAnswersJson: null,
                requiredKeywords: null,
                hint: null,
                referenceText: null,
                referenceKind: null,
                displayOrder: firstPosition + i,
                options: defaultOptions,
                cancellationToken));
        }

        exam.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        return groupDto with { Questions = questionDtos };
    }

    /// <summary>
    /// #273: the Studio's group autosave, on the pool.
    /// <para>
    /// <b>Why this exists.</b> The editor's group card wrote <c>QuestionGroups</c> through
    /// <c>PUT /exams/{id}/groups/{groupId}</c>, a route #265 deleted with the table it wrote - so every
    /// passage, transcript, image-alt and audio-slice edit on a pool paper answered 404 and the screen
    /// kept showing the old value. This is that write, addressed the way the Studio reads it: a group's
    /// identity on a pool paper is its <c>StimulusId</c>, and a solo position's address is
    /// <c>-ExamItemId</c> (<see cref="BuildPoolSectionDto"/>).
    /// </para>
    /// <para>
    /// <b>What it may not change.</b> The stimulus is pool stock shared by every bound item, so this
    /// writes the four chrome fields and the slice, never the items' level, topic or content. A unit is
    /// the paper's own navigation shape, so moving a group between units is a structural edit and the
    /// #157 lock applies; content is cosmetic and is not locked (see <see cref="ExamStructureLock"/>).
    /// </para>
    /// </summary>
    public async Task UpdateExamStudioGroupAsync(
        int examId, int groupId, UpdateQuestionGroupRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var exam = await db.Exams.FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken)
            ?? throw new ExamNotFoundException(examId);

        // A legacy paper's group is its own row and keeps its own writer: the two addresses share the
        // integer, and which table holds the id is what decides the shape (#265).
        if (await db.QuestionGroups.AnyAsync(g => g.GroupId == groupId && g.ExamId == examId, cancellationToken))
        {
            await UpdateQuestionGroupAsync(examId, groupId, request, cancellationToken);
            return;
        }

        var (stimulus, positions) = await ResolveStudioGroupAsync(examId, groupId, cancellationToken);
        var section = await RequireOneStudioSectionAsync(exam, positions, request.SectionId, cancellationToken);

        ValidateQuestionGroup(
            exam.Purpose, section.Modality, request.ImagePath, request.ImageAlt,
            request.AudioStartMs, request.AudioEndMs, request.PrepSeconds, request.ResponseSeconds);

        var unitNumber = request.UnitNumber > 0 ? request.UnitNumber : 1;

        if (stimulus is null)
        {
            // A solo position has no stimulus for anyone to write, and attaching one would re-group the
            // item across every paper that binds it - a pool-wide edit, not a paper edit.
            if (!string.IsNullOrWhiteSpace(request.Passage) || !string.IsNullOrWhiteSpace(request.Transcript)
                || !string.IsNullOrWhiteSpace(request.AudioPath) || !string.IsNullOrWhiteSpace(request.ImagePath))
            {
                throw new AdminExamException(
                    ApiError.Codes.Validation,
                    $"Group {groupId} is one position with no stimulus, so there is no passage, transcript, audio or "
                    + "image of its own to write. Those fields belong to the bundle its items share, and attaching one "
                    + "here would regroup this item on every paper that binds it - add a cluster instead.");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Passage) && string.IsNullOrWhiteSpace(request.Transcript)
                && string.IsNullOrWhiteSpace(request.AudioPath) && string.IsNullOrWhiteSpace(request.ImagePath))
            {
                throw new AdminExamException(
                    ApiError.Codes.Validation,
                    $"Group {groupId} would carry no passage, transcript, audio or image. A pooled group is exactly the "
                    + "stimulus its positions share; remove the positions to remove the group.");
            }

            stimulus.Passage = Normalize(request.Passage);
            stimulus.Transcript = Normalize(request.Transcript);
            stimulus.AudioPath = Normalize(request.AudioPath);
            stimulus.AudioStartMs = request.AudioStartMs;
            stimulus.AudioEndMs = request.AudioEndMs;
            stimulus.ImagePath = Normalize(request.ImagePath);
            stimulus.ImageAlt = Normalize(request.ImageAlt);
            stimulus.PrepSeconds = request.PrepSeconds;
            stimulus.ResponseSeconds = request.ResponseSeconds;
        }

        // The unit is the paper's own: a position that moves between units re-locks what a finished
        // attempt was scored over, so the lock is the one rule that applies to this half of the write.
        if (positions.Any(p => p.UnitNumber != unitNumber))
        {
            await RequireStructureEditableAsync(examId, cancellationToken);
        }

        foreach (var position in positions)
        {
            position.UnitNumber = unitNumber;
        }

        exam.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// #273: "Remove Group" - the positions leave the paper, the stock stays in the pool.
    /// <para>
    /// A binding is the only thing that puts an item on a paper (<c>ExamItems</c>), so removing a group
    /// is deleting its bindings and nothing else: the item, its versions and the stimulus it shares are
    /// pool stock another paper may be sitting on right now. An answered position is refused by name,
    /// because a recorded answer cites the position it was given.
    /// </para>
    /// </summary>
    public async Task DeleteExamStudioGroupAsync(int examId, int groupId, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken)
            ?? throw new ExamNotFoundException(examId);

        if (await db.QuestionGroups.AnyAsync(g => g.GroupId == groupId && g.ExamId == examId, cancellationToken))
        {
            await DeleteQuestionGroupAsync(examId, groupId, cancellationToken);
            return;
        }

        var (_, positions) = await ResolveStudioGroupAsync(examId, groupId, cancellationToken);
        var positionIds = positions.Select(p => p.ExamItemId).ToList();

        var answered = await db.AttemptAnswers.AnyAsync(
                a => positionIds.Contains(a.ExamItemId), cancellationToken)
            || await db.FreeResponses.AnyAsync(
                r => positionIds.Contains(r.ExamItemId), cancellationToken);

        if (answered)
        {
            throw new AdminExamException(
                ApiError.Codes.QuestionHasAttemptAnswers,
                $"Group {groupId} holds a position a learner has answered, so it cannot leave the paper: the answer "
                + "cites that position. Withdraw the paper instead of editing a sitting that already happened.",
                400);
        }

        // Removing positions shrinks the section a finished attempt was scored over (#157).
        await RequireStructureEditableAsync(examId, cancellationToken);

        db.ExamItems.RemoveRange(positions);
        exam.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The positions a Studio group address names. A positive address is a stimulus - the bundle the
    /// Studio draws as one card - and a negative one is a solo position addressed as
    /// <c>-ExamItemId</c>, the same convention <see cref="BuildPoolSectionDto"/> and the sitting use.
    /// </summary>
    private async Task<(Stimulus? Stimulus, List<ExamItem> Positions)> ResolveStudioGroupAsync(
        int examId, int groupId, CancellationToken cancellationToken)
    {
        if (groupId == 0)
        {
            throw new AdminExamException(
                ApiError.Codes.Validation,
                "0 is not a group address. A group is a stimulus id, or a solo position's negated id.");
        }

        if (groupId < 0)
        {
            var solo = -groupId;
            var single = await db.ExamItems
                .Where(i => i.ExamId == examId && i.ExamItemId == solo)
                .ToListAsync(cancellationToken);

            if (single.Count == 0)
            {
                throw new AdminExamException(
                    ApiError.Codes.Validation,
                    $"Group {groupId} names position {solo}, which is not on exam {examId}.");
            }

            return (null, single);
        }

        var stimulus = await db.Stimuli.FirstOrDefaultAsync(s => s.StimulusId == groupId, cancellationToken)
            ?? throw new AdminExamException(
                ApiError.Codes.Validation,
                $"Group {groupId} is not a stimulus and no position of exam {examId} negates to it.");

        var itemIds = await db.Items.AsNoTracking()
            .Where(item => item.StimulusId == groupId)
            .Select(item => item.ItemId)
            .ToListAsync(cancellationToken);

        var positions = await db.ExamItems
            .Where(i => i.ExamId == examId && itemIds.Contains(i.ItemId))
            .ToListAsync(cancellationToken);

        if (positions.Count == 0)
        {
            throw new AdminExamException(
                ApiError.Codes.Validation,
                $"Stimulus {groupId} is bound to no position of exam {examId}, so it is not a group on this paper.");
        }

        return (stimulus, positions);
    }

    /// <summary>
    /// One section for one group, and the section the editor named. A pool group's positions could in
    /// principle be bound into two sections of one paper, and then the card would be two cards; moving
    /// one is a re-binding rather than a group edit, so both cases are refused by name.
    /// </summary>
    private async Task<Section> RequireOneStudioSectionAsync(
        Exam exam, List<ExamItem> positions, int requestedSectionId, CancellationToken cancellationToken)
    {
        var sectionIds = positions.Select(p => p.SectionId).Distinct().ToList();
        if (sectionIds.Count > 1)
        {
            throw new AdminExamException(
                ApiError.Codes.Validation,
                "This group's positions sit in more than one section, so it is not one card. Edit each section's "
                + "positions separately.");
        }

        if (sectionIds[0] != requestedSectionId)
        {
            throw new AdminExamException(
                ApiError.Codes.Validation,
                $"This group's positions are in section {sectionIds[0]}, not {requestedSectionId}. Moving a group "
                + "between sections is a re-binding, not a group edit.");
        }

        return await db.Sections.FirstOrDefaultAsync(
                s => s.SectionId == requestedSectionId && s.CertId == exam.CertId, cancellationToken)
            ?? throw new AdminExamException(
                ApiError.Codes.Validation,
                $"Section with id {requestedSectionId} not found for certificate {exam.CertId}.");
    }

    /// <summary>
    /// The pool's semantic type for a section. A section states a modality, not a semantic type - the
    /// pool's <c>ItemType</c> answers "what does this measure" - so the mapping is by modality, and it
    /// is the same vocabulary <c>CK_Items_Modality_Type</c> accepts.
    /// </summary>
    private static string ItemTypeFor(Section section, bool isFreeResponse) => section.Modality switch
    {
        "listening" => "short_exchange",
        "reading" => "sentence_cloze",
        "speaking" => "descriptive_monologue",
        "writing" => "transactional_correspondence",
        _ => isFreeResponse ? "descriptive_monologue" : "sentence_cloze",
    };
}
