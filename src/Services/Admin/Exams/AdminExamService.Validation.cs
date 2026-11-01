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
    /// #157: the answer key and the option set are what a finished attempt was graded against.
    /// Changing type or moving the question to another group/section re-files it under a different
    /// section total. Everything else on this request is cosmetic.
    /// </summary>
    private static bool ChangesStructure(Question question, UpdateQuestionRequest request)
    {
        if (request.GroupId.HasValue && request.GroupId.Value != question.GroupId)
        {
            return true;
        }

        if (question.QuestionType != request.QuestionType.ToCode())
        {
            return true;
        }

        // The stored set is what the finished attempt was scored against; an incoming set that
        // differs in labels, correctness or text changes that. No options in the request means the
        // caller left the set alone.
        if (request.Options is not { Count: > 0 })
        {
            return false;
        }

        var stored = question.QuestionOptionQuestionNavigations
            .Select(o => (o.Label.Trim().ToUpperInvariant(), o.IsCorrect))
            .OrderBy(o => o.Item1)
            .ToList();
        var incoming = request.Options
            .Select(o => (o.Label.Trim().ToUpperInvariant(), o.IsCorrect))
            .OrderBy(o => o.Item1)
            .ToList();

        return !stored.SequenceEqual(incoming);
    }

    /// <summary>
    /// #157: once an exam has a finished attempt, its structure is what that attempt was scored
    /// against, so it may not change. "Structure" here means anything that changes which questions
    /// exist, which section they belong to, or what the right answer is — adding or deleting a
    /// section's questions or groups, moving a question, and editing an option set. Cosmetic fields
    /// (names, stems, explanations, hints, notes, audio/passage stimuli) stay editable: they cannot
    /// change a recorded score. Create/delete entry points call this; they used to check only that
    /// the individual question had answers, which let the section's shape drift.
    /// </summary>
    private async Task RequireStructureEditableAsync(int examId, CancellationToken cancellationToken)
    {
        var finishedAttemptCount = await db.ExamAttempts.AsNoTracking()
            .CountAsync(a => a.ExamId == examId && (a.Status == "submitted" || a.Status == "graded"), cancellationToken);

        if (finishedAttemptCount > 0)
        {
            throw new AdminExamException(
                ApiError.Codes.ExamHasFinishedAttempts,
                $"This exam's structure is locked: {finishedAttemptCount} learner {(finishedAttemptCount == 1 ? "attempt has" : "attempts have")} already finished it. " +
                "Adding, removing or moving questions, and changing answer options, would change the score those attempts were given. " +
                "Name, description, internal note, stem, explanation and hint remain editable.",
                400);
        }
    }

    private static void ValidateQuestionGroup(string examPurpose, string sectionModality, string? imagePath, string? imageAlt, int? audioStartMs, int? audioEndMs, int? prepSeconds, int? responseSeconds)
    {
        if (examPurpose == ExamPurpose.SpeakingPractice.ToCode() && sectionModality != "speaking")
        {
            throw new AdminExamException(ApiError.Codes.Validation, "A speaking-practice exam can only hold speaking sections.");
        }

        if (examPurpose == ExamPurpose.WritingPractice.ToCode() && sectionModality != "writing")
        {
            throw new AdminExamException(ApiError.Codes.Validation, "A writing-practice exam can only hold writing sections.");
        }

        var normImg = Normalize(imagePath);
        var normAlt = Normalize(imageAlt);

        if (normImg != null && normAlt == null)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "ImageAlt is required when ImagePath is provided.");
        }

        if (normImg != null && normImg.Length > 400)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "ImagePath cannot exceed 400 characters.");
        }

        if (normAlt != null && normAlt.Length > 400)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "ImageAlt cannot exceed 400 characters.");
        }

        if ((audioStartMs.HasValue && !audioEndMs.HasValue) || (!audioStartMs.HasValue && audioEndMs.HasValue))
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Both AudioStartMs and AudioEndMs must be specified together.");
        }

        if (audioStartMs.HasValue && audioEndMs.HasValue)
        {
            if (audioStartMs.Value < 0 || audioEndMs.Value <= audioStartMs.Value)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "AudioStartMs must be >= 0 and AudioEndMs must be > AudioStartMs.");
            }
        }

        if (prepSeconds.HasValue && prepSeconds.Value < 0)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "PrepSeconds must be greater than or equal to 0.");
        }

        if (responseSeconds.HasValue && responseSeconds.Value <= 0)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "ResponseSeconds must be greater than 0.");
        }
    }

    private async Task SaveSectionCriteriaAsync(
        int sectionId,
        int skillId,
        int certId,
        IReadOnlyList<int> criterionIds,
        CancellationToken cancellationToken)
    {
        var requestedIds = criterionIds.Distinct().ToHashSet();
        if (requestedIds.Count != criterionIds.Count)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Rubric criteria must not contain duplicates.");
        }

        var validIds = await db.GradingCriteria
            .Where(c => requestedIds.Contains(c.CriterionId) && c.CertId == certId && c.SkillId == skillId)
            .Select(c => c.CriterionId)
            .ToListAsync(cancellationToken);
        if (validIds.Count != requestedIds.Count)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Rubric criteria must belong to the question's certificate and skill.");
        }

        var existing = await db.SectionCriteria
            .Where(sc => sc.SectionId == sectionId)
            .ToListAsync(cancellationToken);
        var validIdSet = validIds.ToHashSet();
        db.SectionCriteria.RemoveRange(existing.Where(sc => !validIdSet.Contains(sc.CriterionId)));

        foreach (var criterionId in validIds)
        {
            if (existing.All(sc => sc.CriterionId != criterionId))
            {
                db.SectionCriteria.Add(new SectionCriterion
                {
                    SectionId = sectionId,
                    CriterionId = criterionId,
                    SkillId = skillId
                });
            }
        }
    }

    private static void ValidateQuestionInputs(
        string stem,
        int difficulty,
        QuestionTypeDto type,
        int? prepSeconds,
        int? responseSeconds,
        int? minWords,
        int? maxWords,
        string? hint,
        IReadOnlyList<AdminQuestionOptionInput>? options,
        int? maxSelections,
        bool allowNumber,
        string? acceptedAnswersJson,
        string? requiredKeywords)
    {
        if (string.IsNullOrWhiteSpace(stem))
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Question stem is required.");
        }

        if (difficulty < 1 || difficulty > 5)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Difficulty level must be between 1 and 5.");
        }

        if (hint != null && hint.Length > 1000)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Hint must not exceed 1000 characters.");
        }

        if (type != QuestionTypeDto.FreeResponse)
        {
            if (prepSeconds.HasValue || responseSeconds.HasValue)
            {
                throw new AdminExamException(ApiError.Codes.Validation, $"PrepSeconds and ResponseSeconds must be null for {type} questions.");
            }
        }

        if (type != QuestionTypeDto.FreeResponse && type != QuestionTypeDto.InlineCompletion)
        {
            if (minWords.HasValue || maxWords.HasValue)
            {
                throw new AdminExamException(ApiError.Codes.Validation, $"MinWords and MaxWords must be null for {type} questions.");
            }
        }

        if (type == QuestionTypeDto.FreeResponse)
        {
            if (options != null && options.Count > 0)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "Free response questions cannot have options.");
            }

            if (prepSeconds.HasValue && prepSeconds.Value < 0)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "PrepSeconds must be greater than or equal to 0.");
            }

            if (responseSeconds.HasValue && responseSeconds.Value <= 0)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "ResponseSeconds must be greater than 0.");
            }

            if (minWords.HasValue && minWords.Value <= 0)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "MinWords must be greater than 0.");
            }

            if (maxWords.HasValue && maxWords.Value < (minWords ?? 1))
            {
                throw new AdminExamException(ApiError.Codes.Validation, "MaxWords must be greater than or equal to MinWords.");
            }
        }
        else if (type is QuestionTypeDto.Mcq or QuestionTypeDto.Mcq_3)
        {
            if (options == null || options.Count < 2)
            {
                throw new AdminExamException(ApiError.Codes.Validation, $"{type} questions must have at least 2 options.");
            }

            var correctCount = options.Count(o => o.IsCorrect);
            if (correctCount != 1)
            {
                throw new AdminExamException(ApiError.Codes.Validation, $"{type} questions must have exactly 1 correct option.");
            }

            ValidateOptionEntries(options);
        }
        else if (type == QuestionTypeDto.MultiSelect)
        {
            if (options == null || options.Count < 2)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "Multi-select questions must have at least 2 options.");
            }

            var correctCount = options.Count(o => o.IsCorrect);
            if (correctCount < 1)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "Multi-select questions must have at least 1 correct option.");
            }

            if (maxSelections.HasValue && maxSelections.Value < 2)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "MaxSelections must be at least 2 when specified.");
            }

            ValidateOptionEntries(options);
        }
        else if (type == QuestionTypeDto.InlineCompletion)
        {
            if (minWords.HasValue && minWords.Value <= 0)
            {
                throw new AdminExamException(ApiError.Codes.Validation, "MinWords must be greater than 0.");
            }

            if (maxWords.HasValue && maxWords.Value < (minWords ?? 1))
            {
                throw new AdminExamException(ApiError.Codes.Validation, "MaxWords must be greater than or equal to MinWords.");
            }

            if (options != null && options.Count > 0)
            {
                ValidateOptionEntries(options);
            }
        }
        else if (type is QuestionTypeDto.Dropdown or QuestionTypeDto.DragDrop)
        {
            if (options != null && options.Count > 0)
            {
                ValidateOptionEntries(options);
            }
        }
    }

    private static void ValidateOptionEntries(IReadOnlyList<AdminQuestionOptionInput> options)
    {
        foreach (var opt in options)
        {
            if (string.IsNullOrWhiteSpace(opt.Label))
            {
                throw new AdminExamException(ApiError.Codes.Validation, "Option label is required.");
            }

            if (string.IsNullOrWhiteSpace(opt.Text))
            {
                throw new AdminExamException(ApiError.Codes.Validation, "Option text is required.");
            }
        }

        var uniqueLabels = options.Select(o => o.Label.Trim().ToUpperInvariant()).Distinct().Count();
        if (uniqueLabels != options.Count)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Option labels must be unique.");
        }
    }
}
