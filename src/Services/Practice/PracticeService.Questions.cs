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
            .Where(pa => pa.SessionId == sessionId)
            .OrderBy(pa => pa.DisplayOrder)
            .ToListAsync(cancellationToken);

        var isFinished = session.Status == PracticeStatus.Finished.ToCode();
        var itemIds = answers.Select(a => a.ItemId).ToList();

        // #224 / #269: a bookmark names the item. The row id comes along, because unsaving addresses the
        // bookmark itself - the practice page has no other way to name the right row.
        var savedByItemId = itemIds.Count == 0
            ? []
            : await db.SavedQuestions
                .AsNoTracking()
                .Where(sq => sq.UserId == userId && itemIds.Contains(sq.ItemId))
                .ToDictionaryAsync(sq => sq.ItemId, sq => sq.SavedQuestionId, cancellationToken);

        // #187: a pool answer renders the version it pinned, and nothing else decides. The pinned rows
        // are loaded in one query, so a session with no pool answers costs no second round trip.
        var pinned = PlacePinnedVersions(answers, await LoadPinnedVersionsAsync(answers, cancellationToken));
        var stimuli = await LoadStimuliAsync(pinned.Values, cancellationToken);

        return answers.Select(answer =>
            ProjectAnswer(session, answer, isFinished, pinned.GetValueOrDefault(answer.AnswerId),
                stimuli, savedByItemId)).ToList();
    }

    public async Task<PracticeQuestionView> GetQuestionAsync(int userId, int sessionId, int displayOrder, CancellationToken cancellationToken = default)
    {
        var query = db.PracticeAnswers
            .AsNoTracking()
            .Include(pa => pa.Session)
                .ThenInclude(s => s.Section)
            .Include(pa => pa.Session)
                .ThenInclude(s => s.Topic)
            .Where(pa => pa.SessionId == sessionId && pa.Session.UserId == userId);

        PracticeAnswer? answer;
        if (displayOrder <= 0)
        {
            // #253: "unanswered" is "carries no content", not "has no grade" - a productive pool
            // answer is stored ungraded and would otherwise be served as the next question forever.
            // Ordered rows then filtered in memory, so the shared method stays the only definition.
            answer = (await query.OrderBy(pa => pa.DisplayOrder).ToListAsync(cancellationToken))
                .FirstOrDefault(pa => !IsAnswerRecorded(pa));
            if (answer is null)
            {
                answer = await query.OrderBy(pa => pa.DisplayOrder).FirstOrDefaultAsync(cancellationToken);
            }
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

        var savedQuestionId = await db.SavedQuestions
            .Where(sq => sq.UserId == userId && sq.ItemId == answer.ItemId)
            .Select(sq => (int?)sq.SavedQuestionId)
            .FirstOrDefaultAsync(cancellationToken);

        return ProjectAnswer(
            answer.Session, answer, isFinished, pinned.GetValueOrDefault(answer.AnswerId), stimulus,
            savedQuestionId is { } iid && answer.ItemId is { } itemId2 ? new Dictionary<int, int> { [itemId2] = iid } : []);
    }

    /// <summary>
    /// #186/#187 / #269: one projection, and it renders the version the answer pinned. There is no
    /// second shape to fall back to: a practice answer names an item and version, and the legacy
    /// question row it could have rendered is gone. A row with no pin (a lesson micro-check answer,
    /// which names only its card) renders an empty stem, which is what it rendered before.
    /// </summary>
    private static PracticeQuestionView ProjectAnswer(
        PracticeSession session,
        PracticeAnswer answer,
        bool isFinished,
        ItemVersion? pinned,
        IReadOnlyDictionary<int, Stimulus> stimuli,
        IReadOnlyDictionary<int, int> savedByItemId)
    {
        var (_, sectionName) = ScopeLabels(session);
        // #253: one definition, so the pool's ungraded productive answer reads as answered here too.
        var isAnswered = AnswerContent.IsAnswered(
            answer.SelectedOptionId, answer.SelectedLabel, answer.ResponseText, answer.SelectedOptionIdsJson);

        IReadOnlyList<PracticeQuestionOption> options = [];
        int? selectedOptionId = null;
        int? correctOptionId = null;
        string? explanation = null;
        string? transcript = null;
        string? passage = null;
        string? audioPath = null;
        string? imagePath = null;
        string? imageAlt = null;
        int? audioStartMs = null;
        int? audioEndMs = null;
        var questionType = QuestionType.Mcq.ToCode();
        int? maxSelections = null;
        var allowNumber = true;
        string? acceptedAnswersJson = null;
        string? requiredKeywords = null;
        int? minWords = null;
        int? maxWords = null;

        if (pinned is not null)
        {
            // #272: the render shape (and the fields it needs) come from the version's own declaration,
            // so a multi_select keeps its label set, a blank keeps its accepted answers, and a
            // productive task keeps them all null. One rule, shared with the grader.
            questionType = RenderTypeOf(pinned);
            maxSelections = pinned.MaxSelections;
            allowNumber = pinned.AllowNumber;
            acceptedAnswersJson = pinned.AcceptedAnswersJson;
            requiredKeywords = pinned.RequiredKeywords;
            minWords = pinned.MinWords;
            maxWords = pinned.MaxWords;

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

        // #269: a pool answer records HintUsed from the pinned version's hint, so the reload must read
        // the hint from that same version. Consulting only the legacy row left the flag true beside a
        // null hint - the learner was told they had used a hint they could not see.
        var hintText = answer.HintUsed ? pinned?.Hint : null;
        int? savedQuestionId = answer.ItemId is { } iid && savedByItemId.TryGetValue(iid, out var idForItem)
            ? idForItem
            : null;
        var isSaved = savedQuestionId.HasValue;

        return new PracticeQuestionView(
            session.SessionId,
            sectionName,
            null,
            answer.DisplayOrder,
            session.QuestionCount,
            pinned?.Stem ?? string.Empty,
            null,
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
            (isAnswered || isFinished) && correctOptionId is { } key ? [key] : [],
            answer.ItemVersionId,
            savedQuestionId);
    }

    /// <summary>
    /// #253: the projection carries no raw label — a pool answer's label is already decoded into
    /// <see cref="PracticeQuestionView.SelectedOptionId"/> — so the four answerable columns reduce to
    /// three here and the one shared definition still decides.
    /// </summary>
    private static bool IsAnsweredView(PracticeQuestionView question) =>
        AnswerContent.IsAnswered(question.SelectedOptionId, null, question.ResponseText, question.SelectedOptionIdsJson);

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
            currentQuestion = questions.FirstOrDefault(q => !IsAnsweredView(q)) ?? questions[0];
        }

        var navigator = questions.Select(item => new PracticeNavigatorItem(
            item.QuestionId,
            item.DisplayOrder,
            IsAnsweredView(item),
            item.IsCorrect,
            item.IsSaved,
            item.DisplayOrder == currentQuestion.DisplayOrder,
            item.ItemVersionId,
            item.SavedQuestionId
        )).ToList();

        return new PracticeSessionTake(session, currentQuestion, questions, navigator);
    }
}
