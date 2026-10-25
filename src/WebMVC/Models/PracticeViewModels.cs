using Contracts;

namespace WebMVC.Models;

public sealed record PracticeCatalogViewModel(PracticeCatalogDto Catalog, string? ErrorMessage = null);

public sealed class StartPracticeFormModel
{
    public int SectionId { get; set; }

    public int QuestionCount { get; set; } = 20;

    public string QuestionSource { get; set; } = "all";

    public string FeedbackMode { get; set; } = "immediate";
}

public sealed record PracticeTakeViewModel(
    PracticeSessionDto Session,
    PracticeQuestionDto Question,
    string? ErrorMessage = null,
    IReadOnlyList<PracticeQuestionDto>? Questions = null,
    IReadOnlyList<PracticeNavigatorItemDto>? Navigator = null);

public sealed class SubmitAnswerFormModel
{
    public int? SelectedOptionId { get; set; }

    public string? ResponseText { get; set; }

    public string? SelectedOptionIdsJson { get; set; }

    public List<int>? SelectedOptionIds { get; set; }

    public string? Action { get; set; }
}
