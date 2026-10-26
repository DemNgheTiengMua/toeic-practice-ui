namespace BusinessObjects;

public sealed record AttemptAnswerSheetItem(
    int QuestionId,
    int? SelectedOptionId,
    string? SelectedOptionIdsJson,
    string? ResponseText,
    bool IsFlagged);
