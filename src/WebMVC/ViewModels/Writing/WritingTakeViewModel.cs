using Contracts;

namespace WebMVC.Models;

public sealed record WritingTakeViewModel(
    WritingTakeDto Take,
    /// <summary>
    /// #239: the server's own word-count expression, handed to the composer so the live count and the
    /// stored count are one rule rather than two that agree today.
    /// </summary>
    string WordCountPattern,
    string? ErrorMessage = null)
{
    public int AnsweredCount { get; } = Take.Questions.Count(item => item.IsAnswered);
    public IReadOnlyDictionary<int, string> NavigatorClasses { get; } = Take.Questions.ToDictionary(
        item => item.QuestionId, item => item.IsCurrent ? "is-current" : item.IsAnswered ? "is-answered" : "is-unanswered");
}
