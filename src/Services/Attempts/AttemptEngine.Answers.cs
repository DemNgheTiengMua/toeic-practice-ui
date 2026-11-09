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
        var attempt = await LoadSittingAttemptAsync(db.ExamAttempts, attemptId, userId, cancellationToken);

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

            // #186: an answer that pinned a version chooses among that version's four options, not
            // among legacy QuestionOptions rows. The option id the projection rendered is decoded
            // back to its label and stored, so the key it is judged against is the pinned version's.
            if (answer.ItemVersionId is int pinnedVersionId)
            {
                var pinnedVersion = await db.ItemVersions
                    .AsNoTracking()
                    .SingleOrDefaultAsync(v => v.ItemVersionId == pinnedVersionId, cancellationToken)
                    ?? throw new AttemptEngineException(
                        AttemptEngineException.PaperNotAssembled,
                        $"Item version {pinnedVersionId} pinned by this answer no longer exists.");

                var label = selectedOptionId.HasValue ? OptionLabelOrNull(selectedOptionId.Value, pinnedVersionId) : null;
                if (selectedOptionId.HasValue && label is null)
                {
                    throw new AttemptEngineException(AttemptEngineException.InvalidOption, "Invalid option for this question.");
                }

                answer.SelectedLabel = label;
                answer.SelectedOptionId = null;
                answer.SelectedOptionIdsJson = selectedOptionIdsJson;
                answer.ResponseText = responseText;
                answer.IsCorrect = IsKeyLabel(pinnedVersion, label);
            }
            else
            {
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
        }

        answer.IsFlagged = isFlagged;
        attempt.LastQuestionId = questionId;
        // #216 / PLC-13: answering is activity, so it moves the placement's inactivity clock. A mock
        // has no LastActivityAt rule and the guard returns before touching it.
        await MarkPlacementActivityAsync(attempt, cancellationToken);

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
        var attempt = await LoadSittingAttemptAsync(db.ExamAttempts, attemptId, userId, cancellationToken);

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
        // #216 / PLC-13: a draft is activity too; otherwise a learner who writes for an hour without
        // submitting loses the sitting at the 48-hour mark measured from their last click.
        await MarkPlacementActivityAsync(attempt, cancellationToken, savedAt);
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
            // #186: a lazily created row pins the same version attempt creation would have pinned.
            var bound = await FindBoundPositionAsync(attempt.ExamId, questionId, cancellationToken);
            answer = new AttemptAnswer
            {
                AttemptId = attemptId,
                QuestionId = questionId,
                ExamId = attempt.ExamId,
                ItemId = bound?.ItemId,
                ItemVersionId = bound?.ItemVersionId,
                IsFlagged = isFlagged
            };
            db.AttemptAnswers.Add(answer);
        }

        response.ResponseText = responseText ?? string.Empty;
        response.Status = FreeResponseStatus.Draft.ToCode();
        response.DraftSavedAt = DateTimeOffset.UtcNow;
        if (answer is not null)
        {
            // a productive version has no options at all, so neither the legacy option id nor the
            // pinned label can hold a choice for a free response
            answer.SelectedOptionId = null;
            answer.SelectedLabel = null;
            answer.IsFlagged = isFlagged;
        }

        attempt.LastQuestionId = questionId;
        // #216 / PLC-13: the free-response save path is the third write that counts as activity.
        await MarkPlacementActivityAsync(attempt, cancellationToken);
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
        var attempt = await LoadSittingAttemptAsync(db.ExamAttempts, attemptId, userId, cancellationToken);

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
