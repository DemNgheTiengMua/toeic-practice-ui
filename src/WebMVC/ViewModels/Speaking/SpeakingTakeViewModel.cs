using Contracts;

namespace WebMVC.Models;

public sealed record SpeakingTakeViewModel(
    SpeakingTakeDto Take,
    string? ErrorMessage = null)
{
    public int ProgressPercent { get; } = Take.SavedCount * 100 / Math.Max(1, Take.TotalQuestions);
    public string HasAudio { get; } = (!string.IsNullOrEmpty(Take.Question.AudioPath)).ToString().ToLowerInvariant();
    public AudioEquipmentCheckModel AudioEquipment { get; } = new(ShowInput: true, ShowOutput: false);
    public IReadOnlyDictionary<int, string> NavigatorClasses { get; } = Take.Questions.ToDictionary(
        item => item.QuestionId, item => item.IsCurrent ? "is-current" : item.IsAnswered ? "is-answered" : "is-unanswered");
}
