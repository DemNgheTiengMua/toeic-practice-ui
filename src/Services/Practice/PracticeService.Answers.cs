using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class PracticeService
{

    public async Task<string?> RevealHintAsync(int userId, int sessionId, int questionId, CancellationToken cancellationToken = default)
    {
        // #269: the address is the answer, not the question. A pool answer has no question row, so the
        // row is resolved by the item version - the same rule SubmitAnswerAsync already uses.
        var answer = await db.PracticeAnswers
            .Include(pa => pa.Session)
            .Where(pa => pa.SessionId == sessionId && pa.Session.UserId == userId
                && pa.ItemVersionId == questionId)
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

        // #269: a practice answer always names the version it pinned, so the hint is always the
        // version's - there is no legacy question row to fall back to.
        return answer.ItemVersionId is int versionId
            ? await db.ItemVersions.AsNoTracking()
                .Where(v => v.ItemVersionId == versionId)
                .Select(v => v.Hint)
                .SingleAsync(cancellationToken)
            : null;
    }

    public async Task<GradedPracticeAnswerResult> SubmitAnswerAsync(int userId, int sessionId, int questionId, SubmitPracticeAnswerInput input, CancellationToken cancellationToken = default)
    {
        // #187 / #269: the address is the answer, not the question. A pool item has no question row, so
        // the service resolves the answer by the item version the client was handed.
        var answer = await db.PracticeAnswers
            .Include(pa => pa.Session)
                .ThenInclude(s => s.Section)
            .Where(pa => pa.SessionId == sessionId
                && pa.Session.UserId == userId
                // #240: a lesson sitting's rows are graded by SubmitLessonMicroCheckAsync, which is the
                // only writer of a card answer. A drill submission addressing one would be graded from
                // a version that row does not have.
                && pa.Session.LessonId == null
                && pa.ItemVersionId == questionId)
            .SingleOrDefaultAsync(cancellationToken)            ?? throw new PracticeException(PracticeException.QuestionNotFound, $"Question {questionId} was not found in session {sessionId}.");

        if (answer.Session.Status != PracticeStatus.InProgress.ToCode())
        {
            throw new PracticeException(PracticeException.SessionAlreadyFinished, "Answers cannot be submitted to a finished session.");
        }

        // #166: one question, one grade. A re-submission used to overwrite the recorded answer and the
        // session's correct count, rewrite a path step's accuracy when it was the last question, and
        // skip mastery - so the session and the mastery row described two different answers, and a
        // miss could be turned into a hit after the fact. The interface already disables an answered
        // question; this is the server saying the same thing.
        // #253: "answered" is content, not a grade. A productive pool answer is stored ungraded, so
        // testing IsCorrect alone let a second submission overwrite a response that already exists.
        if (IsAnswerRecorded(answer))
        {
            throw new PracticeException(
                PracticeException.AlreadyAnswered,
                $"Question {questionId} has already been answered in this session. Continue with the next question.");
        }

        // #187: the pinned version decides the grade, exactly as it decides the render.
        var pinned = await db.ItemVersions.AsNoTracking()
            .SingleAsync(v => v.ItemVersionId == answer.ItemVersionId, cancellationToken);

        int? correctOptionId = PinnedCorrectOptionId(pinned.ItemVersionId, pinned);
        var isFreeResponse = AttemptEngine.IsProductive(pinned);
        var explanation = pinned.Explanation;

        var optionIdsJson = input.SelectedOptionIdsJson;
        if (string.IsNullOrWhiteSpace(optionIdsJson) && input.SelectedOptionIds is { Count: > 0 })
        {
            optionIdsJson = System.Text.Json.JsonSerializer.Serialize(input.SelectedOptionIds);
        }

        if (!AttemptEngine.IsProductive(pinned))
        {
            // #187: the four options carry derived ids, so the chosen one is a label on the pinned
            // version. An id that names no option of THIS version is refused: the legacy question's
            // own option ids are not a way in, because they do not decode against the version.
            answer.SelectedLabel = null;
            answer.SelectedOptionId = null;
            answer.ResponseText = input.ResponseText;
            answer.SelectedOptionIdsJson = optionIdsJson;

            if (input.SelectedOptionId is { } optionId)
            {
                var index = AttemptEngine.PoolOptionIndex(optionId, pinned.ItemVersionId);
                if (index < 0)
                {
                    throw new PracticeException(PracticeException.InvalidOption, $"Option {optionId} does not belong to item version {pinned.ItemVersionId}.");
                }

                answer.SelectedLabel = AttemptEngine.OptionLabel(index);
                // #272: a multi_select declares its key as a label SET ('A,B'), so one label cannot be
                // the whole answer. Both the chosen set and the key set are compared.
                answer.IsCorrect = EvaluatePinnedResponse(
                    pinned, answer.SelectedLabel, answer.SelectedOptionIdsJson, responseText: null);
            }
            else if (!string.IsNullOrWhiteSpace(answer.SelectedOptionIdsJson))
            {
                // #272: a multi_select submission may name ONLY a set of ids, with no single choice.
                // This branch used to record no verdict at all, so the drill counted the question as
                // answered and never judged it.
                answer.IsCorrect = EvaluatePinnedResponse(
                    pinned, null, answer.SelectedOptionIdsJson, responseText: null);
            }
            else
            {
                answer.IsCorrect = null;
            }
        }
        else if (!IsObjectivelyGradable(pinned))
        {
            // A productive task has no key. A response is recorded and left UNGRADED (IsCorrect null),
            // never true: a version cannot evaluate word count or required keywords without the AI
            // queue, and marking any non-blank text correct made a writing drill report 100% accuracy
            // in the session summary. null means "not judged", which is what this is.
            //
            // #272: the test is "carries no key and no accepted answers", NOT "has no options". An
            // inline_completion is blank-shaped too, and leaving it here recorded no verdict for a
            // question the version could judge.
            answer.SelectedOptionId = null;
            answer.SelectedLabel = null;
            answer.SelectedOptionIdsJson = null;
            answer.ResponseText = string.IsNullOrWhiteSpace(input.ResponseText) ? null : input.ResponseText;
            answer.IsCorrect = null;
        }
        else
        {
            // A blank-shaped version that DOES carry accepted answers: a typed completion is judged by
            // them, exactly as the exam engine judges the same version, so a drill and a sitting cannot
            // disagree about one item.
            answer.SelectedOptionId = null;
            answer.SelectedLabel = null;
            answer.SelectedOptionIdsJson = null;
            answer.ResponseText = string.IsNullOrWhiteSpace(input.ResponseText) ? null : input.ResponseText;
            answer.IsCorrect = answer.ResponseText is null ? null : EvaluatePinnedResponse(pinned, null, null, answer.ResponseText);
        }

        answer.AnsweredAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        // #155: every graded answer feeds the cell's mastery. It is written here, where the grade is
        // known, so the lesson micro-check (which also grades) goes through the same writer and cannot
        // drift. #166: the call is refused above when the answer was already graded, so this runs once
        // per question.
        // #187: a pool item's cell is the mastery unit; it has no legacy category.
        await RecordPracticeAnswerAsync(userId, answer, pinned, cancellationToken);

        // Update correct count on session
        var correctCount = await db.PracticeAnswers
            .CountAsync(pa => pa.SessionId == sessionId && pa.IsCorrect == true, cancellationToken);
        answer.Session.CorrectCount = correctCount;

        // Check if all questions in the session have content. #253: this asks "is every row answered",
        // not "is every row graded". A pool productive answer is stored ungraded forever, so reading
        // IsCorrect here made hasRemaining permanently true: the session never reached Finished, its
        // FinishedAt stayed null, and its learning-path step never closed.
        //
        // The siblings are read once and tested in memory: a session holds at most one answer per item,
        // and EF cannot translate the shared method into SQL - a copied four-column test in a LINQ
        // predicate is exactly the second definition this ticket exists to delete.
        var siblings = await db.PracticeAnswers
            .AsNoTracking()
            .Where(pa => pa.SessionId == sessionId && pa.AnswerId != answer.AnswerId)
            .OrderBy(pa => pa.DisplayOrder)
            .ToListAsync(cancellationToken);

        var hasRemaining = siblings.Any(pa => !IsAnswerRecorded(pa));
        var isLast = !hasRemaining;
        if (isLast)
        {
            answer.Session.Status = PracticeStatus.Finished.ToCode();
            answer.Session.FinishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            if (answer.Session.LearnerPathStepId is { } stepId)
            {
                // #154: the step's close and the unlock now live in one place, shared with the mock
                // exam and lesson writers, so all three cannot drift apart. #232: the step carries its
                // own coordinate, so nothing about the step is read off a template.
                var step = await db.LearnerPathSteps
                    .FirstOrDefaultAsync(s => s.StepId == stepId, cancellationToken);
                if (step != null && step.Status != "done")
                {
                    var accuracy = answer.Session.QuestionCount > 0
                        ? Math.Round((decimal)answer.Session.CorrectCount / answer.Session.QuestionCount * 100m, 2)
                        : 100m;

                    await PathStepCompletion.CloseStepAsync(db, step, accuracy, accuracy, cancellationToken);
                }
            }

            await PruneOldSessionsAsync(userId, answer.Session.CertId, cancellationToken);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        // #150: only an objective answer has options, so only it can be reviewed as a saved question.
        // Filing a failed free response there put a question with "0 options" in the bank.
        // #224: a pool item is filed too, pinning the version that was answered - the same version this
        // answer pinned, so the bank and the drill cannot disagree about what was sat. A free response
        // version is productive and still has no options, so #150's rule holds for it as well.
        int? savedQuestionId = await db.SavedQuestions
            .Where(sq => sq.UserId == userId && sq.ItemId == answer.ItemId)
            .Select(sq => (int?)sq.SavedQuestionId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!isFreeResponse && answer.IsCorrect == false && savedQuestionId == null)
        {
            var filed = new SavedQuestion
            {
                UserId = userId,
                CertId = answer.Session.CertId,
                ItemId = answer.ItemId!.Value,
                ItemVersionId = pinned.ItemVersionId,
                Source = SavedQuestionSource.Incorrect.ToCode(),
                SavedAt = DateTimeOffset.UtcNow
            };
            db.SavedQuestions.Add(filed);
            await db.SaveChangesAsync(cancellationToken);
            savedQuestionId = filed.SavedQuestionId;
        }

        var isSaved = savedQuestionId.HasValue;

        int? nextDisplayOrder = null;
        if (!isLast)
        {
            // The same in-memory siblings the completion check read: "next unanswered" and "anything
            // unanswered" are two orders over one list, not two copies of the test.
            var unanswered = siblings.Where(pa => !IsAnswerRecorded(pa)).ToList();

            nextDisplayOrder = unanswered.FirstOrDefault(pa => pa.DisplayOrder > answer.DisplayOrder)?.DisplayOrder
                ?? unanswered.FirstOrDefault()?.DisplayOrder;

            nextDisplayOrder ??= (answer.DisplayOrder < answer.Session.QuestionCount ? answer.DisplayOrder + 1 : 1);
        }

        return new GradedPracticeAnswerResult(
            answer.IsCorrect,
            PinnedSelectedOptionId(pinned.ItemVersionId, answer.SelectedLabel),
            correctOptionId,
            explanation,
            null,
            isLast,
            nextDisplayOrder,
            isSaved,
            pinned.AcceptedAnswersJson,
            correctOptionId is { } key ? [key] : [],
            answer.ResponseText,
            answer.SelectedOptionIdsJson,
            savedQuestionId);
    }

    /// <summary>
    /// #253: "this row already holds an answer", which is not the same as "it has been graded" — see
    /// <see cref="AnswerContent.IsAnswered"/>. A pool productive answer is answered and ungraded.
    /// </summary>
    private static bool IsAnswerRecorded(PracticeAnswer answer) =>
        AnswerContent.IsAnswered(
            answer.SelectedOptionId, answer.SelectedLabel, answer.ResponseText, answer.SelectedOptionIdsJson);

    /// <summary>
    /// #187: one graded answer, one mastery writer. A legacy answer moves its question's history and
    /// its category's mastery, exactly as before.
    /// <para>
    /// #237: a pool answer moves its <b>cell's</b> mastery, which is the unit the specification defines
    /// (PLC-66). The cell is the item's own (topic, level) — never the session's, because a substituted
    /// sibling-topic draw serves an item from a different cell and crediting the session's cell would
    /// mark a topic the learner did not practise.
    /// </para>
    /// <para>
    /// A productive pool answer is stored ungraded (<c>IsCorrect</c> null) and moves neither: an
    /// unjudged answer is not evidence.
    /// </para>
    /// </summary>
    private async Task RecordPracticeAnswerAsync(
        int userId, PracticeAnswer answer, ItemVersion pinned, CancellationToken cancellationToken)
    {
        if (answer.IsCorrect is not { } correct)
        {
            return;
        }

        // #237: a pool answer moves its CELL's mastery, which is the unit the specification defines
        // (PLC-66). The cell is the item's own (topic, level) — never the session's, because a
        // substituted sibling-topic draw serves an item from a different cell and crediting the
        // session's cell would mark a topic the learner did not practise.
        var cell = await db.Items
            .AsNoTracking()
            .Where(i => i.ItemId == answer.ItemId)
            .Select(i => new { i.TopicId, i.CefrLevel })
            .SingleOrDefaultAsync(cancellationToken);

        if (cell is not null)
        {
            await new MasteryService(db, _clock).RecordAnswerAsync(
                userId, cell.TopicId, cell.CefrLevel, correct, cancellationToken);
        }
    }
}
