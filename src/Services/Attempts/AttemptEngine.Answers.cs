using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class AttemptEngine
{
    public async Task<SaveAttemptAnswerResult> SaveAnswerAsync(
        int attemptId,
        int userId,
        int questionId,
        int? selectedOptionId,
        bool isFlagged,
        Func<int, int, Task>? onAutoSubmitted = null,
        CancellationToken cancellationToken = default,
        string? responseText = null,
        string? selectedOptionIdsJson = null)
    {
        var attempt = await db.ExamAttempts
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        if (attempt.PreflightVerifiedAt == null)
        {
            await EnsurePreflightVerifiedAsync(attempt, cancellationToken);
        }

        // Check if expired on touch -> auto-submit and refuse answer

        if (DateTimeOffset.UtcNow >= attempt.ExpiresAt)
        {
            await SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: true, cancellationToken);
            if (onAutoSubmitted != null)
            {
                await onAutoSubmitted(attempt.AttemptId, userId);
            }
            throw new AttemptEngineException(AttemptEngineException.AttemptExpired, "Exam time has expired and your exam was auto-submitted.");
        }

        await ValidateQuestionSittingStateAsync(attempt, questionId, cancellationToken);

        var answer = await db.AttemptAnswers
            .Include(a => a.Question)
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.QuestionId == questionId, cancellationToken);

        if (answer is null)
        {
            return await SaveFreeResponseInternalAsync(
                attempt, attemptId, questionId, selectedOptionId, responseText, isFlagged, null, cancellationToken);
        }

        if (answer.Question.QuestionType == QuestionType.FreeResponse.ToCode())
        {
            return await SaveFreeResponseInternalAsync(
                attempt, attemptId, questionId, selectedOptionId, responseText, isFlagged, answer, cancellationToken);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(responseText) && (answer.Question.QuestionType == QuestionType.Mcq.ToCode() || answer.Question.QuestionType == QuestionType.Mcq_3.ToCode()))
            {
                throw new AttemptEngineException(AttemptEngineException.InvalidOption, "Multiple-choice questions do not accept free-response text.");
            }

            if (selectedOptionId.HasValue)
            {
                var valid = await db.QuestionOptions
                    .AnyAsync(o => o.OptionId == selectedOptionId.Value && o.QuestionId == questionId, cancellationToken);
                if (!valid)
                {
                    throw new AttemptEngineException(AttemptEngineException.InvalidOption, "Invalid option for this question.");
                }
            }

            answer.SelectedOptionId = selectedOptionId;
            answer.SelectedOptionIdsJson = selectedOptionIdsJson;
            answer.ResponseText = responseText;

            var options = await db.QuestionOptions
                .Where(o => o.QuestionId == questionId)
                .ToListAsync(cancellationToken);
            answer.IsCorrect = EvaluateObjectiveAnswer(
                answer.Question,
                options,
                selectedOptionId,
                selectedOptionIdsJson,
                responseText);
        }

        answer.IsFlagged = isFlagged;
        attempt.LastQuestionId = questionId;

        await db.SaveChangesAsync(cancellationToken);

        var effectiveDeadline = ResolveEffectiveDeadline(attempt);
        var remainingSeconds = Math.Max(0, (int)(effectiveDeadline - DateTimeOffset.UtcNow).TotalSeconds);
        return new SaveAttemptAnswerResult(questionId, selectedOptionId, isFlagged, remainingSeconds, attempt.IsAutoSubmitted);
    }

    public async Task<SaveAttemptDraftResult> SaveDraftAsync(
        int attemptId,
        int userId,
        int questionId,
        string? responseText,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        if (attempt.PreflightVerifiedAt == null)
        {
            await EnsurePreflightVerifiedAsync(attempt, cancellationToken);
        }

        if (DateTimeOffset.UtcNow >= attempt.ExpiresAt)
        {
            await SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: true, cancellationToken);
            throw new AttemptEngineException(AttemptEngineException.AttemptExpired, "Exam time has expired and your exam was auto-submitted.");
        }

        await ValidateQuestionSittingStateAsync(attempt, questionId, cancellationToken);

        var freeResponse = await db.FreeResponses
            .SingleOrDefaultAsync(response =>
                response.AttemptId == attemptId &&
                response.QuestionId == questionId &&
                response.Status == FreeResponseStatus.Draft.ToCode(),
                cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.QuestionNotFound, $"Free-response question {questionId} not found in attempt.");

        var savedAt = DateTimeOffset.UtcNow;
        freeResponse.ResponseText = responseText ?? string.Empty;
        freeResponse.DraftSavedAt = savedAt;
        attempt.LastQuestionId = questionId;
        await db.SaveChangesAsync(cancellationToken);

        var words = freeResponse.ResponseText
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Length;
        return new SaveAttemptDraftResult(freeResponse.FreeResponseId, questionId, savedAt, words);
    }

    private async Task<SaveAttemptAnswerResult> SaveFreeResponseInternalAsync(
        ExamAttempt attempt,
        int attemptId,
        int questionId,
        int? selectedOptionId,
        string? responseText,
        bool isFlagged,
        AttemptAnswer? answer,
        CancellationToken cancellationToken)
    {
        if (selectedOptionId.HasValue)
        {
            throw new AttemptEngineException(AttemptEngineException.InvalidOption, "Free-response questions do not accept multiple-choice options.");
        }

        var response = await db.FreeResponses
            .Include(r => r.Question)
            .SingleOrDefaultAsync(fr => fr.AttemptId == attemptId && fr.QuestionId == questionId, cancellationToken);

        if (response is null)
        {
            if (answer is null)
            {
                throw new AttemptEngineException(AttemptEngineException.QuestionNotFound, $"Question {questionId} not found in attempt.");
            }

            response = new FreeResponse
            {
                AttemptId = attemptId,
                QuestionId = questionId,
                ExamId = answer.ExamId,
                SkillId = answer.Question.SkillId,
                QuestionType = answer.Question.QuestionType,
                Status = FreeResponseStatus.Draft.ToCode(),
                SubmittedAt = null
            };
            db.FreeResponses.Add(response);
        }

        if (answer is null)
        {
            answer = new AttemptAnswer
            {
                AttemptId = attemptId,
                QuestionId = questionId,
                ExamId = attempt.ExamId,
                IsFlagged = isFlagged
            };
            db.AttemptAnswers.Add(answer);
        }

        response.ResponseText = responseText ?? string.Empty;
        response.Status = FreeResponseStatus.Draft.ToCode();
        response.DraftSavedAt = DateTimeOffset.UtcNow;
        if (answer is not null)
        {
            answer.SelectedOptionId = null;
            answer.IsFlagged = isFlagged;
        }

        attempt.LastQuestionId = questionId;
        await db.SaveChangesAsync(cancellationToken);

        var effectiveDeadline = ResolveEffectiveDeadline(attempt);
        var remaining = Math.Max(0, (int)(effectiveDeadline - DateTimeOffset.UtcNow).TotalSeconds);
        return new SaveAttemptAnswerResult(questionId, null, isFlagged, remaining, attempt.IsAutoSubmitted);
    }

    public async Task<IReadOnlyList<SaveAttemptAnswerResult>> SaveBatchAnswersAsync(
        int attemptId,
        int userId,
        IReadOnlyList<BatchAnswerItem> answers,
        Func<int, int, Task>? onAutoSubmitted = null,
        CancellationToken cancellationToken = default)
    {
        var attempt = await db.ExamAttempts
            .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.UserId == userId, cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.AttemptNotFound, $"Attempt {attemptId} not found.");

        if (attempt.Status != AttemptStatus.InProgress.ToCode())
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptNotActive, "Exam attempt is not in progress.");
        }

        if (attempt.PreflightVerifiedAt == null)
        {
            await EnsurePreflightVerifiedAsync(attempt, cancellationToken);
        }

        if (DateTimeOffset.UtcNow >= attempt.ExpiresAt)
        {
            await SubmitAttemptAsync(attempt.AttemptId, userId, isAutoSubmit: true, cancellationToken);
            if (onAutoSubmitted != null)
            {
                await onAutoSubmitted(attempt.AttemptId, userId);
            }
            throw new AttemptEngineException(AttemptEngineException.AttemptExpired, "Exam time has expired and your exam was auto-submitted.");
        }

        var phaseDeadline = ToUtcOffset(attempt.PhaseDeadlineAt);
        if (phaseDeadline.HasValue && DateTimeOffset.UtcNow >= phaseDeadline.Value)
        {
            throw new AttemptEngineException(AttemptEngineException.AttemptExpired, "Exam phase time has expired.");
        }

        var results = new List<SaveAttemptAnswerResult>(answers.Count);
        foreach (var item in answers)
        {
            var res = await SaveAnswerAsync(
                attemptId,
                userId,
                item.QuestionId,
                item.SelectedOptionId,
                item.IsFlagged,
                onAutoSubmitted,
                cancellationToken,
                item.ResponseText,
                item.SelectedOptionIdsJson);
            results.Add(res);
        }

        return results;
    }
}
