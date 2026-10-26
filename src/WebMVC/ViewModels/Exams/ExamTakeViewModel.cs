using Contracts;

namespace WebMVC.Models;

public sealed record ExamTakeViewModel(AttemptTakeDto Take, string? ErrorMessage = null)
{
    public bool HasErrorMessage { get; } = !string.IsNullOrEmpty(ErrorMessage);

    public ExamTakePresentation Display { get; } = ExamTakePresentation.Create(Take);
}
