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
            .Include(s => s.Topic)
            .Where(s => s.SessionId == sessionId && s.UserId == userId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PracticeException(PracticeException.SessionNotFound, $"Practice session {sessionId} was not found.");

        var answers = await db.PracticeAnswers
            .AsNoTracking()
            .Include(pa => pa.Question)
                .ThenInclude(q => q!.Group)
            .Include(pa => pa.Question)
                .ThenInclude(q => q!.Category)
            .Include(pa => pa.Question)
                .ThenInclude(q => q!.QuestionOptionQuestionNavigations)
            .Where(pa => pa.SessionId == sessionId)
            .OrderBy(pa => pa.DisplayOrder)
            .ToListAsync(cancellationToken);

        var isFinished = session.Status == PracticeStatus.Finished.ToCode();
        var questionIds = answers.Where(a => a.QuestionId.HasValue).Select(a => a.QuestionId!.Value).ToList();

        var savedQuestionIds = questionIds.Count == 0
            ? []
            : await db.SavedQuestions
                .AsNoTracking()
                .Where(sq => sq.UserId == userId && questionIds.Contains(sq.QuestionId))
                .Select(sq => sq.QuestionId)
                .ToHashSetAsync(cancellationToken);

        // #187: a pool answer renders the version it pinned, and nothing else decides. The pinned rows
        // are loaded in one query, so a session with no pool answers costs no second round trip.
        var pinned = PlacePinnedVersions(answers, await LoadPinnedVersionsAsync(answers, cancellationToken));
        var stimuli = await LoadStimuliAsync(pinned.Values, cancellationToken);

        return answers.Select(answer =>
            ProjectAnswer(session, answer, isFinished, pinned.GetValueOrDefault(answer.AnswerId),
                stimuli, savedQuestionIds)).ToList();
    }

    public async Task<PracticeQuestionView> GetQuestionAsync(int userId, int sessionId, int displayOrder, CancellationToken cancellationToken = default)
    {
        var query = db.PracticeAnswers
            .AsNoTracking()
            .Include(pa => pa.Session)
                .ThenInclude(s => s.Section)
            .Include(pa => pa.Session)
                .ThenInclude(s => s.Topic)
            .Include(pa => pa.Question)
                .ThenInclude(q => q!.Group)
            .Include(pa => pa.Question)
                .ThenInclude(q => q!.Category)
            .Include(pa => pa.Question)
                .ThenInclude(q => q!.QuestionOptionQuestionNavigations)
            .Where(pa => pa.SessionId == sessionId && pa.Session.UserId == userId);

        PracticeAnswer? answer;
        if (displayOrder <= 0)
        {
            answer = await query
                .Where(pa => pa.SelectedOptionId == null && pa.SelectedLabel == null && pa.IsCorrect == null)
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

        var isFinished = answer.Session.Status == PracticeStatus.Finished.ToCode();
        var pinned = PlacePinnedVersions([answer], await LoadPinnedVersionsAsync([answer], cancellationToken));
        var stimulus = await LoadStimuliAsync(pinned.Values, cancellationToken);

        var isSaved = answer.QuestionId is { } questionId
            && await db.SavedQuestions.AnyAsync(sq => sq.UserId == userId && sq.QuestionId == questionId, cancellationToken);

        return ProjectAnswer(
            answer.Session, answer, isFinished, pinned.GetValueOrDefault(answer.AnswerId), stimulus,
            isSaved ? [answer.QuestionId!.Value] : []);
    }

    /// <summary>
    /// #186/#187: one projection for both shapes. A pinned answer renders its version's stem, options,
    /// key and explanation; an unpinned one renders the question row it was answered from. Every field
    /// that is not scored content — word limits, type, audio slice — stays where it already lives.
    /// </summary>
    private static PracticeQuestionView ProjectAnswer(
        PracticeSession session,
        PracticeAnswer answer,
        bool isFinished,
        ItemVersion? pinned,
        IReadOnlyDictionary<int, Stimulus> stimuli,
        IReadOnlyCollection<int> savedQuestionIds)
    {
        var q = answer.Question;
        var (_, sectionName) = ScopeLabels(session);
        var isAnswered = answer.SelectedOptionId.HasValue
            || answer.SelectedLabel is not null
            || answer.IsCorrect.HasValue;

        IReadOnlyList<PracticeQuestionOption> options;
        int? selectedOptionId;
        int? correctOptionId = null;
        string? explanation = null;
        string? transcript;
        string? passage;
        string? audioPath;
        string? imagePath;
        string? imageAlt;
        int? audioStartMs = null;
        int? audioEndMs = null;
        var questionType = q?.QuestionType ?? "mcq";
        int? maxSelections = q?.MaxSelections;
        var allowNumber = q?.AllowNumber ?? true;
        string? acceptedAnswersJson = q?.AcceptedAnswersJson;
        string? requiredKeywords = q?.RequiredKeywords;
        int? minWords = q?.MinWords;
        int? maxWords = q?.MaxWords;

        if (pinned is not null)
        {
            // #187: a version is either four options with a key, or a productive task with none.
            // That is the whole of what a pool item offers a drill, so the type is read from the
            // shape rather than pretended from the legacy vocabulary.
            questionType = AttemptEngine.IsProductive(pinned) ? QuestionType.FreeResponse.ToCode() : QuestionType.Mcq.ToCode();
            maxSelections = null;
            allowNumber = true;
            acceptedAnswersJson = null;
            requiredKeywords = null;
            minWords = null;
            maxWords = null;

            options = PinnedOptions(pinned.ItemVersionId, pinned);
            selectedOptionId = PinnedSelectedOptionId(pinned.ItemVersionId, answer.SelectedLabel);
            if (isAnswered || isFinished)
            {
                correctOptionId = PinnedCorrectOptionId(pinned.ItemVersionId, pinned);
                explanation = pinned.Explanation;
            }

            var stimulus = stimuli.GetValueOrDefault(pinned.ItemId);
            transcript = stimulus?.Transcript;
            passage = stimulus?.Passage;
            audioPath = stimulus?.AudioPath;
            imagePath = stimulus?.ImagePath;
            imageAlt = stimulus?.ImageAlt;
        }
        else
        {
            options = (q?.QuestionOptionQuestionNavigations ?? [])
                .OrderBy(o => o.Label)
                .Select(o => new PracticeQuestionOption(o.OptionId, o.Label, o.Text))
                .ToList();

            selectedOptionId = answer.SelectedOptionId;
            if (isAnswered || isFinished)
            {
                correctOptionId = q?.QuestionOptionQuestionNavigations.FirstOrDefault(o => o.IsCorrect)?.OptionId;
                explanation = q?.QuestionType == QuestionType.FreeResponse.ToCode()
                    ? FreeResponseExplanation(q!, answer.ResponseText)
                    : q?.Explanation;
            }

            transcript = q?.Group?.Transcript;
            passage = q?.Group?.Passage;
            audioPath = q?.Group?.AudioPath;
            imagePath = q?.Group?.ImagePath;
            imageAlt = q?.Group?.ImageAlt;
            audioStartMs = q?.Group?.AudioStartMs;
            audioEndMs = q?.Group?.AudioEndMs;
        }

        var hintText = answer.HintUsed ? q?.Hint : null;
        var isSaved = answer.QuestionId is { } qid && savedQuestionIds.Contains(qid);

        return new PracticeQuestionView(
            session.SessionId,
            sectionName,
            answer.QuestionId,
            answer.DisplayOrder,
            session.QuestionCount,
            pinned?.Stem ?? q?.Stem ?? string.Empty,
            q?.Category?.Name,
            hintText,
            answer.HintUsed,
            options,
            selectedOptionId,
            answer.IsCorrect,
            correctOptionId,
            explanation,
            transcript,
            passage,
            audioPath,
            imagePath,
            imageAlt,
            isSaved,
            audioStartMs,
            audioEndMs,
            questionType,
            maxSelections,
            allowNumber,
            acceptedAnswersJson,
            requiredKeywords,
            minWords,
            maxWords,
            answer.ResponseText,
            AttemptEngine.ParseOptionIds(answer.SelectedOptionIdsJson, answer.SelectedOptionId).ToList(),
            answer.SelectedOptionIdsJson,
            (isAnswered || isFinished)
                ? pinned is not null
                    ? correctOptionId is { } key ? [key] : []
                    : q?.QuestionOptionQuestionNavigations.Where(o => o.IsCorrect).Select(o => o.OptionId).ToList() ?? []
                : [],
            answer.ItemVersionId);
    }

    /// <summary>The stimuli the pinned versions point at, keyed by item, so a passage is read once.</summary>
    private async Task<Dictionary<int, Stimulus>> LoadStimuliAsync(
        IEnumerable<ItemVersion> versions, CancellationToken cancellationToken)
    {
        var itemIds = versions.Select(v => v.ItemId).Distinct().ToList();
        if (itemIds.Count == 0)
        {
            return [];
        }

        return (await db.Items
            .AsNoTracking()
            .Where(i => itemIds.Contains(i.ItemId) && i.StimulusId != null)
            .Select(i => new { i.ItemId, Stimulus = i.Stimulus! })
            .ToListAsync(cancellationToken))
            .ToDictionary(i => i.ItemId, i => i.Stimulus);
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
            item.DisplayOrder == currentQuestion.DisplayOrder,
            item.ItemVersionId
        )).ToList();

        return new PracticeSessionTake(session, currentQuestion, questions, navigator);
    }
}
