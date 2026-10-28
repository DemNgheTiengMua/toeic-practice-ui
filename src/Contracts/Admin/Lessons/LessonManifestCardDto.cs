
namespace Contracts;

public sealed record LessonManifestCardDto(
    string Type,
    string Title,
    string ContentMarkdown,
    string? QuestionPrompt = null,
    string? OptionA = null,
    string? OptionB = null,
    string? OptionC = null,
    string? OptionD = null,
    string? CorrectOption = null,
    string? Explanation = null,
    string? Rationale = null);
