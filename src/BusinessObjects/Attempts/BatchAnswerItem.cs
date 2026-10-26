namespace BusinessObjects;

public sealed record BatchAnswerItem(
    int QuestionId,
    int? SelectedOptionId,
    bool IsFlagged,
    string? ResponseText = null,
    string? SelectedOptionIdsJson = null);
