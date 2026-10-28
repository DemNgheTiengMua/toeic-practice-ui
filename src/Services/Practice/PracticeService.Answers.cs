using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class PracticeService
{

    public async Task<string?> RevealHintAsync(int userId, int sessionId, int questionId, CancellationToken cancellationToken = default)
    {
        var answer = await db.PracticeAnswers
            .Include(pa => pa.Session)
            .Include(pa => pa.Question)
            .Where(pa => pa.SessionId == sessionId && pa.Session.UserId == userId && pa.QuestionId == questionId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PracticeException(PracticeException.QuestionNotFound, $"Question {questionId} was not found in session {sessionId}.");

        if (answer.Session.Status != PracticeStatus.InProgress.ToCode())
        {
            throw new PracticeException(PracticeException.SessionAlreadyFinished, "Hints cannot be revealed in a finished session.");
        }

        if (!answer.HintUsed)
        {
            answer.HintUsed = true;
            await db.SaveChangesAsync(cancellationToken);
        }

        return answer.Question.Hint;
    }

    public async Task<GradedPracticeAnswerResult> SubmitAnswerAsync(int userId, int sessionId, int questionId, SubmitPracticeAnswerInput input, CancellationToken cancellationToken = default)
    {
        var answer = await db.PracticeAnswers
            .Include(pa => pa.Session)
                .ThenInclude(s => s.Section)
            .Include(pa => pa.Question)
                .ThenInclude(q => q.Group)
            .Include(pa => pa.Question)
                .ThenInclude(q => q.QuestionOptionQuestionNavigations)
            .Where(pa => pa.SessionId == sessionId && pa.Session.UserId == userId && pa.QuestionId == questionId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PracticeException(PracticeException.QuestionNotFound, $"Question {questionId} was not found in session {sessionId}.");

        if (answer.Session.Status != PracticeStatus.InProgress.ToCode())
        {
            throw new PracticeException(PracticeException.SessionAlreadyFinished, "Answers cannot be submitted to a finished session.");
        }

        var q = answer.Question;
        var allOptions = await db.QuestionOptions
            .AsNoTracking()
            .Where(o => o.QuestionId == questionId)
            .ToListAsync(cancellationToken);

        int? correctOptionId = allOptions.FirstOrDefault(o => o.IsCorrect)?.OptionId;

        var optionIdsJson = input.SelectedOptionIdsJson;
        if (string.IsNullOrWhiteSpace(optionIdsJson) && input.SelectedOptionIds is { Count: > 0 })
        {
            optionIdsJson = System.Text.Json.JsonSerializer.Serialize(input.SelectedOptionIds);
        }

        if (input.SelectedOptionId is { } optionId)
        {
            var option = allOptions.FirstOrDefault(o => o.OptionId == optionId)
                ?? throw new PracticeException(PracticeException.InvalidOption, $"Option {optionId} does not belong to question {questionId}.");

            answer.SelectedOptionId = optionId;
            if (input.SelectedOptionIds is { Count: > 0 })
            {
                foreach (var optId in input.SelectedOptionIds)
                {
                    if (!allOptions.Any(o => o.OptionId == optId))
                    {
                        throw new PracticeException(PracticeException.InvalidOption, $"Option {optId} does not belong to question {questionId}.");
                    }
                }
            }
            answer.ResponseText = input.ResponseText;
            answer.SelectedOptionIdsJson = optionIdsJson;
            answer.IsCorrect = AttemptEngine.EvaluateObjectiveAnswer(
                q,
                allOptions,
                optionId,
                optionIdsJson,
                input.ResponseText);
        }
        else if (!string.IsNullOrWhiteSpace(input.ResponseText) || !string.IsNullOrWhiteSpace(optionIdsJson))
        {
            answer.SelectedOptionId = null;
            if (input.SelectedOptionIds is { Count: > 0 })
            {
                foreach (var optId in input.SelectedOptionIds)
                {
                    if (!allOptions.Any(o => o.OptionId == optId))
                    {
                        throw new PracticeException(PracticeException.InvalidOption, $"Option {optId} does not belong to question {questionId}.");
                    }
                }
            }
            answer.ResponseText = input.ResponseText;
            answer.SelectedOptionIdsJson = optionIdsJson;
            answer.IsCorrect = AttemptEngine.EvaluateObjectiveAnswer(
                q,
                allOptions,
                null,
                optionIdsJson,
                input.ResponseText);
        }
        else
        {
            answer.SelectedOptionId = null;
            answer.ResponseText = null;
            answer.SelectedOptionIdsJson = null;
            answer.IsCorrect = null;
        }

        await db.SaveChangesAsync(cancellationToken);

        // Update correct count on session
        var correctCount = await db.PracticeAnswers
            .CountAsync(pa => pa.SessionId == sessionId && pa.IsCorrect == true, cancellationToken);
        answer.Session.CorrectCount = correctCount;

        // Check if all questions in the session have been evaluated
        var hasRemaining = await db.PracticeAnswers
            .AnyAsync(pa => pa.SessionId == sessionId && pa.AnswerId != answer.AnswerId && pa.SelectedOptionId == null && pa.IsCorrect == null, cancellationToken);
        var isLast = !hasRemaining;
        if (isLast)
        {
            answer.Session.Status = PracticeStatus.Finished.ToCode();
            answer.Session.FinishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            if (answer.Session.LearnerPathStepId is { } stepId)
            {
                var step = await db.LearnerPathSteps
                    .Include(s => s.LearnerPath)
                    .FirstOrDefaultAsync(s => s.StepId == stepId, cancellationToken);
                if (step != null && step.Status != "done")
                {
                    step.Status = "done";
                    step.CompletedAt = DateTimeOffset.UtcNow;
                    step.Score = answer.Session.QuestionCount > 0
                        ? Math.Round((decimal)answer.Session.CorrectCount / answer.Session.QuestionCount * 100m, 2)
                        : 100m;

                    var allSteps = await db.LearnerPathSteps
                        .Include(s => s.PathModuleItem)
                            .ThenInclude(pmi => pmi.PathModule)
                        .Where(s => s.LearnerPathId == step.LearnerPathId)
                        .OrderBy(s => s.PathModuleItem.PathModule.DisplayOrder)
                        .ThenBy(s => s.PathModuleItem.DisplayOrder)
                        .ToListAsync(cancellationToken);

                    var nextLocked = allSteps.FirstOrDefault(s => s.Status == "locked");
                    if (nextLocked != null)
                    {
                        nextLocked.Status = "available";
                    }

                    if (allSteps.All(s => s.Status == "done"))
                    {
                        step.LearnerPath.Status = "completed";
                    }

                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            var certId = answer.Session.Section.CertId;
            await PruneOldSessionsAsync(userId, certId, cancellationToken);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        bool isSaved = false;
        if (answer.IsCorrect == false)
        {
            var existingSq = await db.SavedQuestions
                .FirstOrDefaultAsync(sq => sq.UserId == userId && sq.QuestionId == questionId, cancellationToken);
            if (existingSq == null)
            {
                db.SavedQuestions.Add(new SavedQuestion
                {
                    UserId = userId,
                    QuestionId = questionId,
                    Source = SavedQuestionSource.Incorrect.ToCode(),
                    SavedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync(cancellationToken);
            }
            isSaved = true;
        }
        else
        {
            isSaved = await db.SavedQuestions
                .AnyAsync(sq => sq.UserId == userId && sq.QuestionId == questionId, cancellationToken);
        }

        int? nextDisplayOrder = null;
        if (!isLast)
        {
            var nextUnanswered = await db.PracticeAnswers
                .Where(pa => pa.SessionId == sessionId && pa.AnswerId != answer.AnswerId && pa.DisplayOrder > answer.DisplayOrder && pa.SelectedOptionId == null && pa.IsCorrect == null)
                .OrderBy(pa => pa.DisplayOrder)
                .Select(pa => (int?)pa.DisplayOrder)
                .FirstOrDefaultAsync(cancellationToken);

            nextDisplayOrder = nextUnanswered ?? await db.PracticeAnswers
                .Where(pa => pa.SessionId == sessionId && pa.AnswerId != answer.AnswerId && pa.SelectedOptionId == null && pa.IsCorrect == null)
                .OrderBy(pa => pa.DisplayOrder)
                .Select(pa => (int?)pa.DisplayOrder)
                .FirstOrDefaultAsync(cancellationToken);

            nextDisplayOrder ??= (answer.DisplayOrder < answer.Session.QuestionCount ? answer.DisplayOrder + 1 : 1);
        }

        return new GradedPracticeAnswerResult(
            answer.IsCorrect,
            answer.SelectedOptionId,
            correctOptionId,
            q.Explanation,
            q.Group.Transcript,
            isLast,
            nextDisplayOrder,
            isSaved,
            q.AcceptedAnswersJson,
            allOptions.Where(o => o.IsCorrect).Select(o => o.OptionId).ToList(),
            answer.ResponseText,
            answer.SelectedOptionIdsJson);
    }
}
