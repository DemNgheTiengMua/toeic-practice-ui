using Contracts;

namespace WebMVC.Models;

public sealed record WritingReviewViewModel(
    WritingReviewDto Review,
    string? ErrorMessage = null);
