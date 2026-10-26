using Contracts;

namespace WebMVC.Models;

public sealed record ExamReviewOptionViewModel(AttemptReviewOptionDto Option, bool IsSelected, string CssClass);
