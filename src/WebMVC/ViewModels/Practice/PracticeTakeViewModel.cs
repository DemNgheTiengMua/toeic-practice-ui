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

    /// <summary>#166: a refused re-submission must reach a learner whose browser did not run the AJAX
    /// handler. Every other take view renders its error message; this one had the property and never
    /// printed it, so a scriptless learner was redirected with no explanation.</summary>
    public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);
}
