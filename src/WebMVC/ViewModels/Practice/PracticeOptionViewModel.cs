using Contracts;

namespace WebMVC.Models;

public sealed record PracticeOptionViewModel(PracticeOptionDto Option, bool IsSelected, bool IsCorrect, string CssClass);
