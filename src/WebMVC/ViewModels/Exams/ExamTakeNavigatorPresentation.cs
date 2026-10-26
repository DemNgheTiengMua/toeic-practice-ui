using Contracts;

namespace WebMVC.Models;

public sealed record ExamTakeNavigatorPresentation(
    int QuestionId, int DisplayOrder, bool IsLocked, string NavigatorClass, string PaletteClass, string NavigatorTitle, string PaletteTitle)
{
    internal static ExamTakeNavigatorPresentation Create(NavigatorItemDto item)
    {
        var statusClass = item.IsAnswered ? "is-answered" : "is-unanswered";
        var markClass = item.IsFlagged ? "is-marked" : "";
        var currentClass = item.IsCurrent ? "is-current" : "";
        var stateClass = item.IsCurrent ? "palette-chip--current" : item.IsAnswered ? "palette-chip--answered" : "palette-chip--unanswered";
        var flagClass = item.IsFlagged ? "palette-chip--flagged" : "";
        var title = $"Question {item.DisplayOrder}";
        var flagTitle = item.IsFlagged ? " · Flagged" : "";
        var lockedTitle = item.IsLocked ? " · Locked" : "";
        return new(item.QuestionId, item.DisplayOrder, item.IsLocked,
            $"{statusClass} {markClass} {currentClass} {(item.IsLocked ? "is-locked" : "")}",
            $"{stateClass} {flagClass}",
            title + (item.IsAnswered ? " · Answered" : " · Unanswered") + flagTitle + lockedTitle,
            title + (item.IsAnswered ? " · Answered" : "") + flagTitle + lockedTitle);
    }
}
