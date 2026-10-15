using Contracts;

namespace WebMVC.Models;

public sealed record WritingInstructionsViewModel(
    WritingInstructionsDto Instructions,
    string? ErrorMessage = null);

public sealed record WritingTakeViewModel(
    WritingTakeDto Take,
    string? ErrorMessage = null);

public sealed record SaveWritingDraftAjaxModel(
    int AttemptId,
    int QuestionId,
    string? ResponseText);

public sealed record WritingPendingViewModel(
    WritingGradingPendingDto Pending,
    string? ErrorMessage = null);

public sealed record WritingReviewViewModel(
    WritingReviewDto Review,
    string? ErrorMessage = null);
