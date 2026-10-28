using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class PracticeService
{

    public async Task<IReadOnlyList<PracticeQuestionView>> GetSessionQuestionsAsync(int userId, int sessionId, CancellationToken cancellationToken = default)
    {
        var session = await db.PracticeSessions
            .AsNoTracking()
            .Include(s => s.Section)
            .Where(s => s.SessionId == sessionId && s.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PracticeException(PracticeException.SessionNotFound, $"Practice session {sessionId} was not found.");

        var answers = await db.PracticeAnswers
            .AsNoTracking()
            .Include(pa => pa.Question)
                .ThenInclude(q => q.Group)
            .Include(pa => pa.Question)
                .ThenInclude(q => q.Category)
            .Include(pa => pa.Question)
                .ThenInclude(q => q.QuestionOptionQuestionNavigations)
            .Where(pa => pa.SessionId == sessionId)
            .OrderBy(pa => pa.DisplayOrder)
            .ToListAsync(cancellationToken);

        var isFinished = session.Status == PracticeStatus.Finished.ToCode();
        var questionIds = answers.Select(a => a.QuestionId).ToList();

        var savedQuestionIds = await db.SavedQuestions
            .AsNoTracking()
            .Where(sq => sq.UserId == userId && questionIds.Contains(sq.QuestionId))
            .Select(sq => sq.QuestionId)
            .ToHashSetAsync(cancellationToken);

        var result = new List<PracticeQuestionView>(answers.Count);
        foreach (var answer in answers)
        {
            var q = answer.Question;
            var group = q.Group;
            var isAnswered = answer.SelectedOptionId.HasValue || answer.IsCorrect.HasValue;

            var allOptions = q.QuestionOptionQuestionNavigations
                .OrderBy(o => o.Label)
                .Select(o => new PracticeQuestionOption(o.OptionId, o.Label, o.Text))
                .ToList();

            int? correctOptionId = null;
            string? explanation = null;
            string? transcript = null;

            if (isAnswered || isFinished)
            {
                correctOptionId = q.QuestionOptionQuestionNavigations.FirstOrDefault(o => o.IsCorrect)?.OptionId;
                explanation = q.Explanation;
                transcript = group.Transcript;
            }

            var hintText = answer.HintUsed ? q.Hint : null;
            var isSaved = savedQuestionIds.Contains(q.QuestionId);

            result.Add(new PracticeQuestionView(
                sessionId,
                session.Section.Name,
                q.QuestionId,
                answer.DisplayOrder,
                session.QuestionCount,
                q.Stem,
                q.Category?.Name,
                hintText,
                answer.HintUsed,
                allOptions,
                answer.SelectedOptionId,
                answer.IsCorrect,
                correctOptionId,
                explanation,
                transcript,
                group.Passage,
                group.AudioPath,
                group.ImagePath,
                group.ImageAlt,
                isSaved,
                group.AudioStartMs,
                group.AudioEndMs,
                q.QuestionType ?? "mcq",
                q.MaxSelections,
                q.AllowNumber,
                q.AcceptedAnswersJson,
                q.RequiredKeywords,
                q.MinWords,
                q.MaxWords,
                answer.ResponseText,
                AttemptEngine.ParseOptionIds(answer.SelectedOptionIdsJson, answer.SelectedOptionId).ToList(),
                answer.SelectedOptionIdsJson,
                (isAnswered || isFinished) ? q.QuestionOptionQuestionNavigations.Where(o => o.IsCorrect).Select(o => o.OptionId).ToList() : new List<int>()));
        }

        return result;
    }

    public async Task<PracticeQuestionView> GetQuestionAsync(int userId, int sessionId, int displayOrder, CancellationToken cancellationToken = default)
    {
        var query = db.PracticeAnswers
            .AsNoTracking()
            .Include(pa => pa.Session)
                .ThenInclude(s => s.Section)
            .Include(pa => pa.Question)
                .ThenInclude(q => q.Group)
            .Include(pa => pa.Question)
                .ThenInclude(q => q.Category)
            .Include(pa => pa.Question)
                .ThenInclude(q => q.QuestionOptionQuestionNavigations)
            .Where(pa => pa.SessionId == sessionId && pa.Session.UserId == userId);

        PracticeAnswer? answer;
        if (displayOrder <= 0)
        {
            answer = await query
                .Where(pa => pa.SelectedOptionId == null && pa.IsCorrect == null)
                .OrderBy(pa => pa.DisplayOrder)
                .FirstOrDefaultAsync(cancellationToken)
                ?? await query.OrderBy(pa => pa.DisplayOrder).FirstOrDefaultAsync(cancellationToken);
        }
        else
        {
            answer = await query
                .Where(pa => pa.DisplayOrder == displayOrder)
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (answer is null)
        {
            throw new PracticeException(PracticeException.QuestionNotFound, $"Question at index {displayOrder} was not found in session {sessionId}.");
        }

        var q = answer.Question;
        var group = q.Group;
        var isFinished = answer.Session.Status == PracticeStatus.Finished.ToCode();
        var isAnswered = answer.SelectedOptionId.HasValue || answer.IsCorrect.HasValue;

        var allOptions = await db.QuestionOptions
            .AsNoTracking()
            .Where(o => o.QuestionId == q.QuestionId)
            .OrderBy(o => o.Label)
            .ToListAsync(cancellationToken);

        var options = allOptions
            .Select(o => new PracticeQuestionOption(o.OptionId, o.Label, o.Text))
            .ToList();

        int? correctOptionId = null;
        string? explanation = null;
        string? transcript = null;

        if (isAnswered || isFinished)
        {
            correctOptionId = allOptions.FirstOrDefault(o => o.IsCorrect)?.OptionId;
            explanation = q.Explanation;
            transcript = group.Transcript;
        }

        var hintText = answer.HintUsed ? q.Hint : null;
        var isSaved = await db.SavedQuestions
            .AnyAsync(sq => sq.UserId == userId && sq.QuestionId == q.QuestionId, cancellationToken);

        return new PracticeQuestionView(
            sessionId,
            answer.Session.Section.Name,
            q.QuestionId,
            answer.DisplayOrder,
            answer.Session.QuestionCount,
            q.Stem,
            q.Category?.Name,
            hintText,
            answer.HintUsed,
            options,
            answer.SelectedOptionId,
            answer.IsCorrect,
            correctOptionId,
            explanation,
            transcript,
            group.Passage,
            group.AudioPath,
            group.ImagePath,
            group.ImageAlt,
            isSaved,
            group.AudioStartMs,
            group.AudioEndMs,
            q.QuestionType ?? "mcq",
            q.MaxSelections,
            q.AllowNumber,
            q.AcceptedAnswersJson,
            q.RequiredKeywords,
            q.MinWords,
            q.MaxWords,
            answer.ResponseText,
            AttemptEngine.ParseOptionIds(answer.SelectedOptionIdsJson, answer.SelectedOptionId).ToList(),
            answer.SelectedOptionIdsJson,
            (isAnswered || isFinished) ? allOptions.Where(o => o.IsCorrect).Select(o => o.OptionId).ToList() : new List<int>());
    }

    public async Task<PracticeSessionTake> GetSessionTakeAsync(int userId, int sessionId, int? displayOrder = null, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionInfoAsync(userId, sessionId, cancellationToken);
        var questions = await GetSessionQuestionsAsync(userId, sessionId, cancellationToken);

        if (questions.Count == 0)
        {
            throw new PracticeException(PracticeException.SessionNotFound, $"No questions found in session {sessionId}.");
        }

        PracticeQuestionView currentQuestion;
        if (displayOrder.HasValue && displayOrder.Value > 0)
        {
            currentQuestion = questions.FirstOrDefault(q => q.DisplayOrder == displayOrder.Value) ?? questions[0];
        }
        else
        {
            currentQuestion = questions.FirstOrDefault(q => q.SelectedOptionId == null && q.IsCorrect == null) ?? questions[0];
        }

        var navigator = questions.Select(item => new PracticeNavigatorItem(
            item.QuestionId,
            item.DisplayOrder,
            item.SelectedOptionId.HasValue || item.IsCorrect.HasValue,
            item.IsCorrect,
            item.IsSaved,
            item.DisplayOrder == currentQuestion.DisplayOrder
        )).ToList();

        return new PracticeSessionTake(session, currentQuestion, questions, navigator);
    }
}
