
namespace Contracts;

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
    int DisplayOrder,
    string? Rationale = null
);
