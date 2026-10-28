using Contracts;
using System.Text.Json;

namespace WebMVC.Models;

public sealed class PracticeTakePresentation
{
    public PracticeTakePresentation(PracticeSessionDto session, PracticeQuestionDto question,
        IReadOnlyList<PracticeQuestionDto>? questions, IReadOnlyList<PracticeNavigatorItemDto>? navigator)
    {
        Session = session;
        Question = question;
        Questions = questions ?? [question];
        Navigator = navigator ?? Questions.Select(x => new PracticeNavigatorItemDto(x.QuestionId, x.DisplayOrder,
            x.SelectedOptionId.HasValue || x.IsCorrect.HasValue, x.IsCorrect, x.IsSaved, x.DisplayOrder == question.DisplayOrder)).ToList();
        IsFinished = session.Status == PracticeStatusDto.Finished;
        NavigatorCells = Navigator.Select(item => new PracticeNavigatorCellViewModel(item,
            (item.IsCorrect == true ? "is-correct" : item.IsCorrect == false ? "is-incorrect"
                : IsFinished && !item.IsAnswered ? "is-skipped" : "is-unanswered")
                + (item.IsSaved ? " is-marked" : "") + (item.IsCurrent ? " is-current" : ""),
            $"Question {item.DisplayOrder}" + (item.IsCorrect == true ? " · Correct" : item.IsCorrect == false ? " · Incorrect" : "")
                + (item.IsSaved ? " · Saved" : ""))).ToList();
        TotalCount = Questions.Count;
        AnsweredCount = Questions.Count(x => x.SelectedOptionId.HasValue || x.IsCorrect.HasValue);
        CorrectCount = Questions.Count(x => x.IsCorrect == true);
        IncorrectCount = Questions.Count(x => x.IsCorrect == false);
        SkippedCount = Questions.Count(x => IsFinished && !x.SelectedOptionId.HasValue && !x.IsCorrect.HasValue);
        UnansweredCount = Math.Max(0, TotalCount - AnsweredCount);
        SavedCount = Questions.Count(x => x.IsSaved);
        AccuracyPercent = AnsweredCount > 0 ? (int)Math.Round((double)CorrectCount / AnsweredCount * 100) : 0;
        ProgressPercent = TotalCount > 0 ? (int)Math.Round((double)AnsweredCount / TotalCount * 100) : 0;
        HasListening = session.Modality == ModalityDto.Listening || Questions.Any(x => !string.IsNullOrEmpty(x.AudioPath));
        IsCurrentAnswered = question.SelectedOptionId.HasValue || question.IsCorrect.HasValue;
        IsCurrentSkipped = IsFinished && !IsCurrentAnswered;
        Options = question.Options.Select(option => new PracticeOptionViewModel(option,
            option.OptionId == question.SelectedOptionId, option.OptionId == question.CorrectOptionId,
            "option" + (IsCurrentAnswered
                ? option.OptionId == question.CorrectOptionId ? " option--correct"
                    : option.OptionId == question.SelectedOptionId && question.IsCorrect == false ? " option--wrong" : ""
                : option.OptionId == question.SelectedOptionId ? " is-selected" : ""))).ToList();
        Audio = new(question.AudioPath ?? "", AllowReplay: true, SectionTitle: "Practice Audio", BadgeText: "READY",
            StartMs: question.AudioStartMs, EndMs: question.AudioEndMs, PlayerId: "main-practice-audio", Mode: "study");
        QuestionsJson = JsonSerializer.Serialize(Questions, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
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
}
