namespace BusinessObjects;

public sealed record SaveAttemptAnswerResult(
    int QuestionId,
    int? SelectedOptionId,
    bool IsFlagged,
    int RemainingSeconds,
    bool IsAutoSubmitted);
