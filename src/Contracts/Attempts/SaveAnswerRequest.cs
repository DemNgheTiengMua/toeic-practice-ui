namespace Contracts;

public sealed record SaveAnswerRequest(
    int? SelectedOptionId,
    bool IsFlagged,
    string? ResponseText = null,
    string? SelectedOptionIdsJson = null);
