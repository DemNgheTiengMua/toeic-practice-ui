
namespace Contracts;

public sealed record AdminLessonCardUpsertDto(
    int? CardId,
    string CardType,
    string Title,
    string ContentMarkdown,
    string? QuestionPrompt = null,
    string? OptionA = null,
    string? OptionB = null,
    string? OptionC = null,
    string? OptionD = null,
    string? CorrectOption = null,
    string? Explanation = null,
    string? Rationale = null,
    int DisplayOrder = 0);
