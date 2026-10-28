namespace Contracts;

public sealed record SubmitPracticeAnswerRequest(
    int? SelectedOptionId,
    string? ResponseText = null,
    string? SelectedOptionIdsJson = null,
    IReadOnlyList<int>? SelectedOptionIds = null);
