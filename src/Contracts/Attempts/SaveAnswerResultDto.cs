namespace Contracts;

public sealed record SaveAnswerResultDto(
    int QuestionId,
    int? SelectedOptionId,
    bool IsFlagged,
    int RemainingSeconds,
    bool IsAutoSubmitted);
