using Contracts;

namespace WebMVC.Models;

public sealed record SpeakingReviewViewModel(
    SpeakingReviewDto Review,
    string? ErrorMessage = null);
