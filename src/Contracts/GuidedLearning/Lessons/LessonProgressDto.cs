
namespace Contracts;

public sealed record LessonProgressDto(
    int CardsCompleted,
    int TotalCards,
    bool IsCompleted,
    int? LastViewedCardId,
    DateTimeOffset? FirstOpenedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? UpdatedAt
);
