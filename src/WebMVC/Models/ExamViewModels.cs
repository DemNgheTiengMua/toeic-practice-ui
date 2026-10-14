using Contracts;

namespace WebMVC.Models;

public sealed record ExamIndexViewModel(
    MockExamListViewDto Catalog,
    string? ErrorMessage = null);

public sealed record ExamInstructionsViewModel(
    MockExamInstructionsDto Instructions,
    string? ErrorMessage = null);

public sealed record ExamTakeViewModel(
    AttemptTakeDto Take,
    string? ErrorMessage = null);

public sealed record SaveAnswerFormModel(
    int? SelectedOptionId,
    bool IsFlagged);

public sealed record ExamResultViewModel(
    AttemptResultDto Result,
    string? ErrorMessage = null);

public sealed record ExamHistoryViewModel(
    ExamHistoryDto History,
    string? ErrorMessage = null);

public sealed record ExamReviewViewModel(
    AttemptReviewDto Review,
    string? ErrorMessage = null);

