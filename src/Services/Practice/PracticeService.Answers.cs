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

        // #155: mastery and the cooldown move on the first grade only. Re-submitting the same answer
        // used to be harmless because it was just a column; now it would count one miss twice and push
        // the category a day further out, which is the exact "keep clicking until it is green" the
        // ticket is closing.
        var alreadyGraded = answer.IsCorrect.HasValue;

        int? correctOptionId = allOptions.FirstOrDefault(o => o.IsCorrect)?.OptionId;
        var isFreeResponse = q.QuestionType == QuestionType.FreeResponse.ToCode();
        var explanation = q.Explanation;

        var optionIdsJson = input.SelectedOptionIdsJson;
        if (string.IsNullOrWhiteSpace(optionIdsJson) && input.SelectedOptionIds is { Count: > 0 })
        {
            optionIdsJson = System.Text.Json.JsonSerializer.Serialize(input.SelectedOptionIds);
        }

        if (isFreeResponse)
        {
            // #150: a speaking or writing drill checks the response against the question's declared
            // requirements - the one thing that can be judged without the AI grading queue. A blank
            // response stays a skip, exactly as an unanswered objective question does.
            answer.SelectedOptionId = null;
            answer.SelectedOptionIdsJson = null;
            answer.ResponseText = string.IsNullOrWhiteSpace(input.ResponseText) ? null : input.ResponseText;
            if (answer.ResponseText is null)
            {
                answer.IsCorrect = null;
            }
            else
            {
                var eval = AttemptEngine.EvaluateWritingResponse(q, answer.ResponseText);
                answer.IsCorrect = !eval.IsUnderLength && eval.HasRequiredKeywords;
                explanation = FreeResponseFeedback(eval, q.Explanation);
            }
        }
        else if (input.SelectedOptionId is { } optionId)
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

        // #155: every graded answer feeds the category's mastery and starts or clears the question's own
        // cooldown. It is written here, where the grade is known, so the lesson micro-check (which also
        // grades) goes through the same two writers and cannot drift.
        if (!alreadyGraded)
        {
            await RecordAnswerAsync(userId, q, answer.Session.Section.CertId, answer.IsCorrect, cancellationToken);
        }

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
        // #150: only an objective answer has options, so only it can be reviewed as a saved question.
        // Filing a failed free response there put a question with "0 options" in the bank.
        if (!isFreeResponse && answer.IsCorrect == false)
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
            explanation,
            q.Group.Transcript,
            isLast,
            nextDisplayOrder,
            isSaved,
            q.AcceptedAnswersJson,
            allOptions.Where(o => o.IsCorrect).Select(o => o.OptionId).ToList(),
            answer.ResponseText,
            answer.SelectedOptionIdsJson);
    }

    /// <summary>#150: the explanation a learner sees for a free-response answer - evaluated from the
    /// stored response against the question's own floor and keywords, so re-reading question or summary
    /// shows the same sentence the submit reply did.</summary>
    private static string? FreeResponseExplanation(Question question, string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return question.Explanation;
        }

        var eval = AttemptEngine.EvaluateWritingResponse(question, responseText);
        return FreeResponseFeedback(eval, question.Explanation);
    }

    /// <summary>The immediate feedback a speaking or writing drill can give without the AI queue: how
    /// long the answer was against the question's floor, and which required keywords it missed.</summary>
    private static string? FreeResponseFeedback(WritingEvaluationResult eval, string? questionExplanation)
    {
        var parts = new List<string>();
        if (eval.IsUnderLength)
        {
            parts.Add($"Answer too short: {eval.WordCount} of at least {eval.MinWords} words.");
        }
        if (!eval.HasRequiredKeywords)
        {
            parts.Add($"Missing required keywords: {string.Join(", ", eval.MissingKeywords)}.");
        }
        if (!string.IsNullOrWhiteSpace(questionExplanation))
        {
            parts.Add(questionExplanation);
        }
        return parts.Count > 0 ? string.Join(" ", parts) : null;
    }
}
