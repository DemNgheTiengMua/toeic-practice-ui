using Contracts;

namespace WebMVC.Models;

public sealed record SpeakingInstructionsViewModel(
    SpeakingInstructionsDto Instructions,
    string? ErrorMessage = null);

public sealed record SpeakingTakeViewModel(
    SpeakingTakeDto Take,
    string? ErrorMessage = null);

public sealed record SpeakingPendingViewModel(
    SpeakingGradingPendingDto Pending,
    string? ErrorMessage = null);

public sealed record SpeakingReviewViewModel(
    SpeakingReviewDto Review,
    string? ErrorMessage = null);
