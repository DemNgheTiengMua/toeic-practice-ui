using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class ExamQuickIngestService
{

    public async Task<QuickIngestCommitResultDto> CommitStagedAsync(
        int examId,
        QuickIngestCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "Commit request cannot be null.", 400);
        }

        if (db == null)
        {
            throw new InvalidOperationException("CompanionDbContext is required to commit staged questions.");
        }

        var exam = await db.Exams.Include(e => e.Cert).FirstOrDefaultAsync(e => e.ExamId == examId && !e.IsDeleted, cancellationToken)
            ?? throw new ExamNotFoundException(examId);

        var section = await db.Sections.FirstOrDefaultAsync(s => s.SectionId == request.SectionId && s.CertId == exam.CertId, cancellationToken)
            ?? throw new AdminExamException("SectionNotFound", $"Section with id {request.SectionId} not found for certificate '{exam.Cert.Name}'.", 404);

        if (request.Questions == null || request.Questions.Count == 0)
        {
            throw new AdminExamException(ApiError.Codes.Validation, "No questions provided to commit.", 400);
        }

        // Pre-validate questions
        for (var i = 0; i < request.Questions.Count; i++)
        {
            var q = request.Questions[i];
            if (string.IsNullOrWhiteSpace(q.Stem))
            {
                throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} has an empty stem.", 400);
            }

            var blockingIssue = q.Issues?.FirstOrDefault(iss => iss.Severity == "error");
            if (blockingIssue != null)
            {
                throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} has validation error: {blockingIssue.Message}", 400);
            }

            var qType = q.QuestionType?.ToLowerInvariant() ?? "mcq";
            if (qType == "free_response")
            {
                // Free response: options not required
            }
            else if (qType == "inline_completion")
            {
                if (string.IsNullOrWhiteSpace(q.AcceptedAnswersJson))
                {
                    throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} is inline_completion but has no accepted answers.", 400);
                }
            }
            else if (qType == "multi_select")
            {
                if (q.Options == null || q.Options.Count < 2)
                {
                    throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} must have at least 2 options.", 400);
                }
                if (!q.Options.Any(o => o.IsCorrect))
                {
                    throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} must have at least one correct option.", 400);
                }
            }
            else // mcq / mcq_3
            {
                if (q.Options == null || q.Options.Count < 2)
                {
                    throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} must have at least 2 options.", 400);
                }
                var correctCount = q.Options.Count(o => o.IsCorrect);
                if (correctCount != 1)
                {
                    throw new AdminExamException(ApiError.Codes.Validation, $"Question {i + 1} must have exactly one correct option (found {correctCount}).", 400);
                }
            }
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var isReplace = string.Equals(request.Mode, "replace", StringComparison.OrdinalIgnoreCase);
        int currentDisplayOrder = 1;

        if (isReplace)
        {
            var existingGroups = await db.QuestionGroups
                .Include(g => g.QuestionGroups)
                    .ThenInclude(q => q.QuestionOptionQuestionNavigations)
                .Where(g => g.ExamId == examId && g.SectionId == request.SectionId)
                .ToListAsync(cancellationToken);

            var existingQuestionIds = existingGroups.SelectMany(g => g.QuestionGroups.Select(q => q.QuestionId)).ToList();

            var hasAnswers = await db.AttemptAnswers.AnyAsync(a => existingQuestionIds.Contains(a.QuestionId), cancellationToken)
                || await db.FreeResponses.AnyAsync(r => existingQuestionIds.Contains(r.QuestionId), cancellationToken);

            if (hasAnswers)
            {
                throw new AdminExamException(ApiError.Codes.QuestionHasAttemptAnswers, "Cannot replace section content because completed or active learner attempts exist.", 400);
            }

            foreach (var g in existingGroups)
            {
                foreach (var q in g.QuestionGroups)
                {
                    var saved = await db.SavedQuestions.Where(s => s.QuestionId == q.QuestionId).ToListAsync(cancellationToken);
                    if (saved.Count > 0) db.SavedQuestions.RemoveRange(saved);

                    var pa = await db.PracticeAnswers.Where(p => p.QuestionId == q.QuestionId).ToListAsync(cancellationToken);
                    if (pa.Count > 0) db.PracticeAnswers.RemoveRange(pa);

                    db.QuestionOptions.RemoveRange(q.QuestionOptionQuestionNavigations);
                    db.Questions.Remove(q);
                }
                db.QuestionGroups.Remove(g);
            }

            await db.SaveChangesAsync(cancellationToken);
            currentDisplayOrder = 1;
        }
        else
        {
            var maxGroupOrder = await db.QuestionGroups
                .Where(g => g.ExamId == examId && g.SectionId == request.SectionId)
                .Select(g => (int?)g.DisplayOrder)
                .MaxAsync(cancellationToken) ?? 0;

            var maxQuestionOrder = await db.Questions
                .Where(q => q.ExamId == examId && q.SectionId == request.SectionId)
                .Select(q => (int?)q.DisplayOrder)
                .MaxAsync(cancellationToken) ?? 0;

            currentDisplayOrder = Math.Max(maxGroupOrder, maxQuestionOrder) + 1;
        }

        var categories = await db.QuestionCategories
            .Where(c => c.CertId == exam.CertId)
            .ToDictionaryAsync(c => c.Code, c => c.CategoryId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var groupsCreated = 0;
        var questionsCreated = 0;

        QuestionGroup? activeGroup = null;
        string? activePassage = null;
        int? activeGroupNum = null;

        foreach (var q in request.Questions)
        {
            var needsNewGroup = activeGroup == null ||
                (!string.IsNullOrWhiteSpace(q.Passage) && !string.Equals(activePassage, q.Passage, StringComparison.Ordinal)) ||
                (q.GroupNumber.HasValue && q.GroupNumber != activeGroupNum);

            if (needsNewGroup)
            {
                activeGroup = new QuestionGroup
                {
                    ExamId = examId,
                    CertId = exam.CertId,
                    SectionId = request.SectionId,
                    DisplayOrder = currentDisplayOrder,
                    Passage = q.Passage,
                    Transcript = q.Transcript,
                    UnitNumber = 1
                };
                db.QuestionGroups.Add(activeGroup);
                await db.SaveChangesAsync(cancellationToken);
                groupsCreated++;

                activePassage = q.Passage;
                activeGroupNum = q.GroupNumber;
            }

            int? categoryId = null;
            if (!string.IsNullOrWhiteSpace(q.CategoryCode) && categories.TryGetValue(q.CategoryCode, out var cid))
            {
                categoryId = cid;
            }

            var qType = q.QuestionType?.ToLowerInvariant() ?? "mcq";

            var question = new Question
            {
                ExamId = examId,
                SectionId = request.SectionId,
                SkillId = section.SkillId,
                GroupId = activeGroup!.GroupId,
                Stem = q.Stem.Trim(),
                DifficultyLevel = q.DifficultyLevel is >= 1 and <= 5 ? q.DifficultyLevel : 3,
                QuestionType = qType,
                Explanation = q.Explanation?.Trim(),
                CategoryId = categoryId,
                PrepSeconds = q.PrepSeconds,
                ResponseSeconds = q.ResponseSeconds,
                MinWords = q.MinWords,
                MaxWords = q.MaxWords,
                MaxSelections = q.MaxSelections,
                AllowNumber = q.AllowNumber,
                AcceptedAnswersJson = q.AcceptedAnswersJson,
                RequiredKeywords = q.RequiredKeywords?.Trim(),
                Hint = q.Hint?.Trim(),
                DisplayOrder = currentDisplayOrder
            };

            db.Questions.Add(question);
            await db.SaveChangesAsync(cancellationToken);
            questionsCreated++;

            if (q.Options != null && q.Options.Count > 0)
            {
                foreach (var opt in q.Options)
                {
                    var option = new QuestionOption
                    {
                        QuestionId = question.QuestionId,
                        QuestionType = question.QuestionType,
                        Label = opt.Label.Trim().ToUpperInvariant(),
                        Text = opt.Text.Trim(),
                        IsCorrect = opt.IsCorrect,
                        Rationale = opt.Rationale?.Trim()
                    };
                    db.QuestionOptions.Add(option);
                }
            }

            currentDisplayOrder++;
        }

        exam.UpdatedAt = clock?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return new QuickIngestCommitResultDto(
            Success: true,
            QuestionsCommitted: questionsCreated,
            GroupsCreated: groupsCreated,
            Message: $"Successfully committed {questionsCreated} questions to section '{section.Name}'.");
    }
}
