using Contracts;

namespace WebMVC.Models;

public sealed record WritingTakeViewModel(
    WritingTakeDto Take,
    string? ErrorMessage = null)
{
    public int AnsweredCount { get; } = Take.Questions.Count(item => item.IsAnswered);
    public IReadOnlyDictionary<int, string> NavigatorClasses { get; } = Take.Questions.ToDictionary(
        item => item.QuestionId, item => item.IsCurrent ? "is-current" : item.IsAnswered ? "is-answered" : "is-unanswered");
}
