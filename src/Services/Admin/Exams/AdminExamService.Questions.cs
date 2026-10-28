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

    public async Task<AdminExamQuestionsDto> GetExamQuestionsAsync(int examId, CancellationToken cancellationToken = default)
    {
        var exam = await db.Exams.AsNoTracking()
            .Include(e => e.Cert)
            .FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);

        if (exam == null)
        {
            throw new ExamNotFoundException(examId);
        }

        var groups = await db.QuestionGroups.AsNoTracking()
            .Include(g => g.Section)
            .Include(g => g.QuestionGroups)
                .ThenInclude(q => q.QuestionOptionQuestionNavigations)
            .Include(g => g.QuestionGroups)
                .ThenInclude(q => q.Category)
            .Where(g => g.ExamId == examId)
            .OrderBy(g => g.DisplayOrder)
            .ToListAsync(cancellationToken);

        var allQuestionIds = groups.SelectMany(g => g.QuestionGroups.Select(q => q.QuestionId)).ToList();
        var answeredQuestionIds = new HashSet<int>(await db.AttemptAnswers.AsNoTracking()
            .Where(a => allQuestionIds.Contains(a.QuestionId))
            .Select(a => a.QuestionId)
            .Concat(db.FreeResponses.AsNoTracking().Where(r => allQuestionIds.Contains(r.QuestionId)).Select(r => r.QuestionId))
            .ToListAsync(cancellationToken));

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
                q.DifficultyLevel,
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
                RequiredKeywords: q.RequiredKeywords)).ToList(),
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

        ValidateQuestionInputs(request.Stem, request.DifficultyLevel, request.QuestionType, request.PrepSeconds, request.ResponseSeconds, request.MinWords, request.MaxWords, request.Hint, request.Options, request.MaxSelections, request.AllowNumber, request.AcceptedAnswersJson, request.RequiredKeywords);

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
            DifficultyLevel = request.DifficultyLevel,
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
            question.DifficultyLevel,
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
            RequiredKeywords: question.RequiredKeywords);
    }

    public async Task<AdminQuestionDto> UpdateQuestionAsync(int questionId, UpdateQuestionRequest request, CancellationToken cancellationToken = default)
    {
        var question = await db.Questions
            .Include(q => q.Group)
                .ThenInclude(g => g.Section)
            .Include(q => q.QuestionOptionQuestionNavigations)
            .FirstOrDefaultAsync(q => q.QuestionId == questionId, cancellationToken);

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

        ValidateQuestionInputs(request.Stem, request.DifficultyLevel, request.QuestionType, request.PrepSeconds, request.ResponseSeconds, request.MinWords, request.MaxWords, request.Hint, request.Options, request.MaxSelections, request.AllowNumber, request.AcceptedAnswersJson, request.RequiredKeywords);

        var hasAnswers = await db.AttemptAnswers.AnyAsync(a => a.QuestionId == questionId, cancellationToken)
            || await db.FreeResponses.AnyAsync(r => r.QuestionId == questionId, cancellationToken);

        if (hasAnswers && question.QuestionType != request.QuestionType.ToCode())
        {
            throw new AdminExamException(ApiError.Codes.QuestionHasAttemptAnswers, "Cannot change question type of a question with recorded attempt answers.");
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

                var hasPracticeAnswers = await db.PracticeAnswers.AnyAsync(p => p.QuestionId == questionId, cancellationToken);
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
        question.DifficultyLevel = request.DifficultyLevel;
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
            question.DifficultyLevel,
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
            RequiredKeywords: question.RequiredKeywords);
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

        var hasAnswers = await db.AttemptAnswers.AnyAsync(a => a.QuestionId == questionId, cancellationToken)
            || await db.FreeResponses.AnyAsync(r => r.QuestionId == questionId, cancellationToken);

        if (hasAnswers)
        {
            throw new AdminExamException(ApiError.Codes.QuestionHasAttemptAnswers, "Cannot delete a question that has recorded learner answers in attempts.", 400);
        }

        var saved = await db.SavedQuestions.Where(s => s.QuestionId == questionId).ToListAsync(cancellationToken);
        if (saved.Count > 0) db.SavedQuestions.RemoveRange(saved);

        var pa = await db.PracticeAnswers.Where(p => p.QuestionId == questionId).ToListAsync(cancellationToken);
        if (pa.Count > 0) db.PracticeAnswers.RemoveRange(pa);

        db.QuestionOptions.RemoveRange(question.QuestionOptionQuestionNavigations);
        db.Questions.Remove(question);

        var exam = await db.Exams.FindAsync([question.ExamId], cancellationToken);
        if (exam != null)
        {
            exam.UpdatedAt = clock.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReorderSectionItemsAsync(int examId, ReorderSectionItemsRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null || request.Items == null || request.Items.Count == 0) return;

        var exam = await db.Exams.FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken);
        if (exam == null) throw new ExamNotFoundException(examId);

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
