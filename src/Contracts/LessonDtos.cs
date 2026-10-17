namespace Contracts;

public sealed record LessonDetailDto(
    int LessonId,
    string Title,
    string? Summary,
    int SkillId,
    int? CategoryId,
    int EstimatedMinutes,
    List<LessonCardDto> Cards
);

public sealed record LessonCardDto(
    int CardId,
    string CardType,
    string Title,
    string ContentMarkdown,
    string? QuestionPrompt,
    string? OptionA,
    string? OptionB,
    string? OptionC,
    string? OptionD,
    string? CorrectOption,
    string? Explanation,
    int DisplayOrder
);

public sealed record LessonProgressDto(
    int CardsCompleted,
    int TotalCards,
    bool IsCompleted,
    int? LastViewedCardId,
    DateTimeOffset? FirstOpenedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? UpdatedAt
);
