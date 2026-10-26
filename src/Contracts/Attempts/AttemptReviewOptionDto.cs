namespace Contracts;

public sealed record AttemptReviewOptionDto(
    int OptionId,
    string Label,
    string Text,
    bool IsCorrect,
    string? Rationale);
