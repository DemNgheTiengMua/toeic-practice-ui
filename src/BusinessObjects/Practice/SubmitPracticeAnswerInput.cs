using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

public sealed record SubmitPracticeAnswerInput(
    int? SelectedOptionId = null,
    string? ResponseText = null,
    string? SelectedOptionIdsJson = null,
    IReadOnlyList<int>? SelectedOptionIds = null);
