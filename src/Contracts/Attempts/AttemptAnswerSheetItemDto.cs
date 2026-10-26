namespace Contracts;

public sealed record AttemptAnswerSheetItemDto(
    int QuestionId,
    int? SelectedOptionId,
    string? SelectedOptionIdsJson,
    string? ResponseText,
    bool IsFlagged);
