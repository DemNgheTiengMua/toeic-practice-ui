namespace Contracts;

public sealed record PracticeNavigatorItemDto(
    int QuestionId,
    int DisplayOrder,
    bool IsAnswered,
    bool? IsCorrect,
    bool IsSaved,
    bool IsCurrent);
