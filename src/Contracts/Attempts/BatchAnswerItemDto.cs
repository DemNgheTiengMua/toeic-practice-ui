namespace Contracts;

public sealed record BatchAnswerItemDto(
    int QuestionId,
    int? SelectedOptionId = null,
    bool IsFlagged = false,
    string? ResponseText = null,
    string? SelectedOptionIdsJson = null);
