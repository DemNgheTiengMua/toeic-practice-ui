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

        // #250: an answer is keyed by whatever identity its question carries. A legacy position names
        // QuestionId; a pool position names ExamItemId, which is the id the projection handed the
        // client. Looking up only the first makes a pool paper look unanswered - measured: saving failed
        // with "Free-response questions do not accept multiple-choice options", because a missing answer
        // fell through to the free-response branch.
        var sittingQuestion = await FindSittingQuestionAsync(attempt, questionId, cancellationToken);

        var answer = sittingQuestion.ExamItemId is int examItemId
            ? await db.AttemptAnswers
                .Include(a => a.Question)
                .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.ExamItemId == examItemId, cancellationToken)
            : await db.AttemptAnswers
                .Include(a => a.Question)
                .SingleOrDefaultAsync(a => a.AttemptId == attemptId && a.QuestionId == questionId, cancellationToken);

        if (answer is null)
        {            return await SaveFreeResponseInternalAsync(
                attempt, attemptId, questionId, selectedOptionId, responseText, isFlagged, null, cancellationToken);
        }
        // #250: the question's type comes from the paper, not from a joined Questions row - a pool
        // answer has no such row.
        if (sittingQuestion.RenderFormat == QuestionType.FreeResponse.ToCode())
        {
            return await SaveFreeResponseInternalAsync(
                attempt, attemptId, questionId, selectedOptionId, responseText, isFlagged, answer, cancellationToken);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(responseText) && (sittingQuestion.RenderFormat == QuestionType.Mcq.ToCode() || sittingQuestion.RenderFormat == QuestionType.Mcq_3.ToCode()))
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
                // #269: a pinned version is graded by its key AND its accepted answers. `IsKeyLabel` alone
                // judges only a lettered choice, so an inline_completion (or any text answer) on the pool
                // could never be correct - the version carries AcceptedAnswersJson and the sitting projects
                // it, but nothing read it back.
                answer.IsCorrect = string.IsNullOrWhiteSpace(responseText)
                    ? IsKeyLabel(pinnedVersion, label)
                    : EvaluatePinnedText(pinnedVersion, responseText);
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

                // #250: a legacy answer still scores against its QuestionOptions rows, so that branch
                // needs the question row itself. The loader carries it for exactly this.
                var legacyQuestion = sittingQuestion.LegacyQuestion
                    ?? answer.Question
                    ?? throw new AttemptEngineException(
                        AttemptEngineException.PaperNotAssembled,
                        "This answer names neither a pinned item version nor a legacy question, so it cannot be scored.");

                var options = await db.QuestionOptions
                    .Where(o => o.QuestionId == (answer.QuestionId ?? questionId))
                    .ToListAsync(cancellationToken);
                answer.IsCorrect = EvaluateObjectiveAnswer(
                    legacyQuestion,
                    options,
                    selectedOptionId,
                    selectedOptionIdsJson,
                    responseText);
            }
        }

        answer.IsFlagged = isFlagged;
        // #250: LastQuestionId is an FK to Questions, so only a legacy position can be stored there.
        // A pool position's id is an ExamItemId, and writing it would violate that FK - measured:
        // "The UPDATE statement conflicted with the FOREIGN KEY constraint FK_Attempts_LastQuestionExam".
        RecordLastPosition(attempt, sittingQuestion);
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

        // #261: the row is addressed by the position the request names, which is a question id on a
        // legacy paper and a roster id on a pool paper.
        var position = await FindSittingQuestionAsync(attempt, questionId, cancellationToken);
        var examItemId = position.ExamItemId;
        var freeResponse = await db.FreeResponses
            .SingleOrDefaultAsync(response =>
                response.AttemptId == attemptId &&
                (examItemId.HasValue
                    ? response.ExamItemId == examItemId
                    : response.QuestionId == questionId) &&
                response.Status == FreeResponseStatus.Draft.ToCode(),
                cancellationToken)
            ?? throw new AttemptEngineException(AttemptEngineException.QuestionNotFound, $"Free-response question {questionId} not found in attempt.");

        var savedAt = DateTimeOffset.UtcNow;
        freeResponse.ResponseText = responseText ?? string.Empty;
        freeResponse.DraftSavedAt = savedAt;
        // #250: see SaveAnswerAsync - the column is an FK to Questions and only a legacy position fits.
        RecordLastPosition(attempt, position);
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

        var position = await FindSittingQuestionAsync(attempt, questionId, cancellationToken);

        var positionExamItemId = position.ExamItemId;
        var response = await db.FreeResponses
            .SingleOrDefaultAsync(fr => fr.AttemptId == attemptId
                && (positionExamItemId.HasValue
                    ? fr.ExamItemId == positionExamItemId
                    : fr.QuestionId == questionId), cancellationToken);

        if (response is null)
        {
            response = new FreeResponse
            {
                AttemptId = attemptId,
                QuestionId = position.ExamItemId is null ? questionId : null,
                ExamItemId = position.ExamItemId,
                ExamId = attempt.ExamId,
                SkillId = position.Section.SkillId,
                QuestionType = QuestionType.FreeResponse.ToCode(),
                Status = FreeResponseStatus.Draft.ToCode(),
                SubmittedAt = null
            };
            db.FreeResponses.Add(response);
        }

        if (answer is null)
        {
            // #186 / #261: a lazily created answer names the same position attempt creation would
            // have, so a pool paper records its roster row and a legacy paper its question.
            answer = new AttemptAnswer
            {
                AttemptId = attemptId,
                QuestionId = position.ExamItemId is null ? questionId : null,
                ExamItemId = position.ExamItemId,
                ExamId = attempt.ExamId,
                ItemId = position.ItemId == 0 ? null : position.ItemId,
                ItemVersionId = position.ItemVersionId,
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

        // #250: same rule as the other two writes - only a legacy position fits LastQuestionId's FK.
        RecordLastPosition(attempt, await FindSittingQuestionAsync(attempt, questionId, cancellationToken));
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
