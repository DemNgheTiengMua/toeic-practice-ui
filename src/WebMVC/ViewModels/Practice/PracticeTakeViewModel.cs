using Contracts;

namespace WebMVC.Models;

public sealed record PracticeTakeViewModel(
    PracticeSessionDto Session,
    PracticeQuestionDto Question,
    string? ErrorMessage = null,
    IReadOnlyList<PracticeQuestionDto>? Questions = null,
    IReadOnlyList<PracticeNavigatorItemDto>? Navigator = null)
{
    public PracticeTakePresentation Take { get; } = new(Session, Question, Questions, Navigator);
}
