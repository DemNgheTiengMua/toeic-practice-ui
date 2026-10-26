namespace BusinessObjects;

public sealed record AttemptReviewOption(
    int OptionId,
    string Label,
    string Text,
    bool IsCorrect,
    string? Rationale);
