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

        var qIds = await db.Questions.Where(q => q.GroupId == groupId).Select(q => q.QuestionId).ToListAsync(cancellationToken);
        var hasAnswers = await db.AttemptAnswers.AnyAsync(a => qIds.Contains(a.QuestionId), cancellationToken)
            || await db.FreeResponses.AnyAsync(r => qIds.Contains(r.QuestionId), cancellationToken);

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

    public async Task<AdminQuestionGroupDto> CreateExamClusterAsync(int examId, CreateExamClusterRequest request, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);
        if (exam == null) throw new ExamNotFoundException(examId);

        var section = await db.Sections.Include(s => s.Skill).FirstOrDefaultAsync(s => s.SectionId == request.SectionId && s.CertId == exam.CertId, cancellationToken);
        if (section == null)
        {
            throw new AdminExamException(ApiError.Codes.Validation, $"Section with id {request.SectionId} not found for certificate {exam.CertId}.");
        }

        var maxOrder = await db.QuestionGroups
            .Where(g => g.ExamId == examId && g.SectionId == request.SectionId)
            .Select(g => (int?)g.DisplayOrder)
            .MaxAsync(cancellationToken) ?? 0;

        var qMaxOrder = await db.Questions
            .Where(q => q.ExamId == examId && q.SectionId == request.SectionId)
            .Select(q => (int?)q.DisplayOrder)
            .MaxAsync(cancellationToken) ?? 0;

        var isFreeResponse = section.Modality is "speaking" or "writing"
            || (section.AllowedComponentTypesJson != null && section.AllowedComponentTypesJson.Contains("free_response") && !section.AllowedComponentTypesJson.Contains("mcq"));

        var group = new QuestionGroup
        {
            ExamId = examId,
            CertId = exam.CertId,
            SectionId = request.SectionId,
            Passage = Normalize(request.Passage),
            Transcript = Normalize(request.Transcript),
            AudioPath = Normalize(request.AudioPath),
            AudioStartMs = request.AudioStartMs,
            AudioEndMs = request.AudioEndMs,
            DisplayOrder = maxOrder + 1,
            UnitNumber = request.UnitNumber > 0 ? request.UnitNumber : 1
        };

        db.QuestionGroups.Add(group);
        await db.SaveChangesAsync(cancellationToken);

        var count = request.QuestionCount > 0 ? Math.Clamp(request.QuestionCount, 1, 10) : (section.ClusterSize > 0 ? section.ClusterSize : 3);
        var questions = new List<Question>();

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
                stem = $"Question {qMaxOrder + i + 1}";
            }

            var qType = isFreeResponse ? QuestionTypeDto.FreeResponse
                : (section.OptionCount == 3 ? QuestionTypeDto.Mcq_3 : QuestionTypeDto.Mcq);

            var q = new Question
            {
                GroupId = group.GroupId,
                ExamId = examId,
                SectionId = request.SectionId,
                SkillId = section.SkillId,
                Stem = stem,
                DifficultyLevel = 2,
                QuestionType = qType.ToCode(),
                Explanation = section.HideOptionText ? "Option A is the most appropriate response." : "Correct answer option A.",
                PrepSeconds = isFreeResponse ? (section.Modality == "speaking" ? 15 : 60) : null,
                ResponseSeconds = isFreeResponse ? (section.Modality == "speaking" ? 45 : 1800) : null,
                MinWords = section.Modality == "writing" ? 150 : null,
                MaxWords = section.Modality == "writing" ? 300 : null,
                DisplayOrder = qMaxOrder + i + 1
            };
            db.Questions.Add(q);
            questions.Add(q);
        }

        await db.SaveChangesAsync(cancellationToken);

        var questionDtos = new List<AdminQuestionDto>();

        foreach (var q in questions)
        {
            var optionsList = new List<AdminQuestionOptionDto>();
            if (!isFreeResponse)
            {
                var optionLabels = section.OptionCount == 3 ? new[] { "A", "B", "C" } : new[] { "A", "B", "C", "D" };
                foreach (var lbl in optionLabels)
                {
                    var opt = new QuestionOption
                    {
                        QuestionId = q.QuestionId,
                        QuestionType = q.QuestionType,
                        Label = lbl,
                        Text = $"Option {lbl}",
                        IsCorrect = lbl == "A",
                        Rationale = lbl == "A" ? "Correct response" : "Distractor response"
                    };
                    db.QuestionOptions.Add(opt);
                    await db.SaveChangesAsync(cancellationToken);
                    optionsList.Add(new AdminQuestionOptionDto(opt.OptionId, q.QuestionId, opt.Label, opt.Text, opt.IsCorrect, opt.Rationale));
                }
            }

            questionDtos.Add(new AdminQuestionDto(
                q.QuestionId,
                group.GroupId,
                examId,
                request.SectionId,
                section.Code,
                section.Name,
                section.SkillId,
                section.Skill?.Code ?? "skill",
                q.Stem,
                q.DifficultyLevel,
                EnumCodes.Parse<QuestionTypeDto>(q.QuestionType),
                q.Explanation,
                q.CategoryId,
                null,
                q.PrepSeconds,
                q.ResponseSeconds,
                q.MinWords,
                q.MaxWords,
                q.Hint,
                q.DisplayOrder,
                clock.GetUtcNow(),
                optionsList,
                HasAnswers: false,
                MaxSelections: q.MaxSelections,
                AllowNumber: q.AllowNumber,
                AcceptedAnswersJson: q.AcceptedAnswersJson,
                RequiredKeywords: q.RequiredKeywords));
        }

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
            questionDtos,
            group.UnitNumber);
    }
}
