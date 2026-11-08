using Contracts;
using System.Text.Json;

namespace WebMVC.Models;

public sealed class PracticeTakePresentation
{
    public PracticeTakePresentation(PracticeSessionDto session, PracticeQuestionDto question,
        IReadOnlyList<PracticeQuestionDto>? questions, IReadOnlyList<PracticeNavigatorItemDto>? navigator)
    {
        Session = session;
        SessionLevelCode = session.CefrLevelCode;
        Question = question;
        Questions = questions ?? [question];
        Navigator = navigator ?? Questions.Select(x => new PracticeNavigatorItemDto(x.QuestionId, x.DisplayOrder,
            IsAnswered(x), x.IsCorrect, x.IsSaved, x.DisplayOrder == question.DisplayOrder,
            x.ItemVersionId, x.SavedQuestionId)).ToList();
        IsFinished = session.Status == PracticeStatusDto.Finished;
        NavigatorCells = Navigator.Select(item => new PracticeNavigatorCellViewModel(item,
            (item.IsCorrect == true ? "is-correct" : item.IsCorrect == false ? "is-incorrect"
                : IsFinished && !item.IsAnswered ? "is-skipped" : "is-unanswered")
                + (item.IsSaved ? " is-marked" : "") + (item.IsCurrent ? " is-current" : ""),
            $"Question {item.DisplayOrder}" + (item.IsCorrect == true ? " · Correct" : item.IsCorrect == false ? " · Incorrect" : "")
                + (item.IsSaved ? " · Saved" : ""))).ToList();
        TotalCount = Questions.Count;
        AnsweredCount = Questions.Count(IsAnswered);
        CorrectCount = Questions.Count(x => x.IsCorrect == true);
        IncorrectCount = Questions.Count(x => x.IsCorrect == false);
        SkippedCount = Questions.Count(x => IsFinished && !IsAnswered(x));
        UnansweredCount = Math.Max(0, TotalCount - AnsweredCount);
        SavedCount = Questions.Count(x => x.IsSaved);
        AccuracyPercent = AnsweredCount > 0 ? (int)Math.Round((double)CorrectCount / AnsweredCount * 100) : 0;
        ProgressPercent = TotalCount > 0 ? (int)Math.Round((double)AnsweredCount / TotalCount * 100) : 0;
        HasListening = session.Modality == ModalityDto.Listening || Questions.Any(x => !string.IsNullOrEmpty(x.AudioPath));
        IsCurrentAnswered = IsAnswered(question);
        IsCurrentSkipped = IsFinished && !IsCurrentAnswered;
        Options = question.Options.Select(option => new PracticeOptionViewModel(option,
            option.OptionId == question.SelectedOptionId, option.OptionId == question.CorrectOptionId,
            "option" + (IsCurrentAnswered
                ? option.OptionId == question.CorrectOptionId ? " option--correct"
                    : option.OptionId == question.SelectedOptionId && question.IsCorrect == false ? " option--wrong" : ""
                : option.OptionId == question.SelectedOptionId ? " is-selected" : ""))).ToList();
        Audio = new(question.AudioPath ?? "", AllowReplay: true, SectionTitle: "Practice Audio", BadgeText: "READY",
            StartMs: question.AudioStartMs, EndMs: question.AudioEndMs, PlayerId: "main-practice-audio", Mode: "study");

        // #238 / U18: the level is revealed only for a question the learner has already locked. The
        // manifest is what the browser actually receives, so stripping the level here — rather than
        // hiding a span — is what keeps a B2 item from reading as A1 and being skipped.
        QuestionsJson = JsonSerializer.Serialize(
            Questions.Select(WithLevelIfLocked), JsonOptions);

        // A pool drill names its cell; a section drill names none and has no level to reveal.
        RevealedLevelCode = IsCurrentAnswered ? CellLevel(question) : null;
        IsShort = session.IsShort;
        ShortNotice = IsShort
            ? $"This drill is short: your cell held {session.QuestionCount} new item{(session.QuestionCount == 1 ? "" : "s")} "
              + "rather than a full session, and it was not padded with repeats."
            : null;

        // #238: "bound to its evidence" — the explanation names the option it explains. A free response
        // has no option to name, so the subject stays null and the view prints the feedback alone.
        var subject = IsCurrentAnswered && question.QuestionType != "free_response"
            ? question.CorrectOptionId is { } key
                ? question.Options.FirstOrDefault(o => o.OptionId == key)
                : null
            : null;
        ExplanationSubjectLabel = subject?.Label;
        ExplanationSubjectText = subject?.Text;
    }

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// #238 / U18: one question as it travels to the browser. An unanswered question loses its level
    /// outright; a locked one receives it, so navigating back to a finished item still shows its level.
    /// <c>with</c> over a record keeps every other field intact, so this cannot silently drop the stem
    /// or the options the page renders.
    /// </summary>
    private PracticeQuestionDto WithLevelIfLocked(PracticeQuestionDto question) =>
        IsAnswered(question)
            ? question with { CefrLevelCode = CellLevel(question) }
            : question with { CefrLevelCode = null };

    /// <summary>A pool item's level is its drilled cell's; a section question's is its own, when it has one.</summary>
    private string? CellLevel(PracticeQuestionDto question) =>
        question.CefrLevelCode ?? SessionLevelCode;

    /// <summary>The drilled cell's level, when the session names a cell at all (#238 / U18).</summary>
    private string? SessionLevelCode { get; }

    public IReadOnlyList<PracticeNavigatorCellViewModel> NavigatorCells { get; }
    public IReadOnlyList<PracticeOptionViewModel> Options { get; }
    public ExamAudioPlayerModel Audio { get; }
    public ExamAudioPlayerModel AudioTemplate { get; } = new("", Mode: "study", TemplateOnly: true);
    public AudioEquipmentCheckModel AudioCheck { get; } = new(ShowInput: false, ShowOutput: true);
    public string TopbarAccuracyDisplay => AnsweredCount > 0 ? $"{AccuracyPercent}%" : "--%";
    public string SidebarAccuracyDisplay => $"{AccuracyPercent}% ({CorrectCount}/{AnsweredCount})";
    public PracticeSessionDto Session { get; }
    public PracticeQuestionDto Question { get; }
    public IReadOnlyList<PracticeQuestionDto> Questions { get; }
    public IReadOnlyList<PracticeNavigatorItemDto> Navigator { get; }
    public bool IsFinished { get; }
    public int TotalCount { get; }
    public int AnsweredCount { get; }
    public int CorrectCount { get; }
    public int IncorrectCount { get; }
    public int SkippedCount { get; }
    public int UnansweredCount { get; }
    public int SavedCount { get; }
    public int AccuracyPercent { get; }
    public int ProgressPercent { get; }
    public bool HasListening { get; }
    public bool IsCurrentAnswered { get; }
    public bool IsCurrentSkipped { get; }
    public string QuestionsJson { get; }

    /// <summary>#238 / U18: the level to reveal, or null while answering or when no cell is named.</summary>
    public string? RevealedLevelCode { get; }

    /// <summary>#235 / #238: a cell that held fewer items than a session asks for is short, not padded.</summary>
    public bool IsShort { get; }

    /// <summary>The sentence a short drill prints, or null for a full one.</summary>
    public string? ShortNotice { get; }

    /// <summary>#238: the option the explanation explains, or null for a free response.</summary>
    public string? ExplanationSubjectLabel { get; }

    /// <summary>The text of that option, so the review names the answer rather than gesturing at it.</summary>
    public string? ExplanationSubjectText { get; }

    /// <summary>#253: one definition for every count on this page — the navigator, the topbar and
    /// the summary must agree about one attempt.</summary>
    private static bool IsAnswered(PracticeQuestionDto question) =>
        AnswerContent.IsAnswered(
            question.SelectedOptionId, null, question.ResponseText, question.SelectedOptionIdsJson);
}
