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

        var qIds = await db.Questions.Where(q => q.GroupId == groupId).Select(q => q.QuestionId).ToListAsync(cancellationToken);
        // #250: QuestionId is nullable, so the predicate is stated on a nullable column. A pool answer
        // carries NULL here and can never match a real question id, which is the correct answer.
        var hasAnswers = await db.AttemptAnswers.AnyAsync(
                a => a.QuestionId != null && qIds.Contains(a.QuestionId.Value), cancellationToken)
            || await db.FreeResponses.AnyAsync(r => r.QuestionId != null && qIds.Contains(r.QuestionId.Value), cancellationToken);

        if (hasAnswers)
        {
            throw new AdminExamException(ApiError.Codes.QuestionHasAttemptAnswers, "Cannot delete a question group containing questions answered in attempts.", 400);
        }

        foreach (var q in group.QuestionGroups)
        {
            var saved = await db.SavedQuestions.Where(s => s.QuestionId == q.QuestionId).ToListAsync(cancellationToken);
            if (saved.Count > 0) db.SavedQuestions.RemoveRange(saved);

            var pa = await db.PracticeAnswers.Where(p => p.QuestionId == q.QuestionId).ToListAsync(cancellationToken);
            if (pa.Count > 0) db.PracticeAnswers.RemoveRange(pa);

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
