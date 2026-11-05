namespace Contracts;

/// <summary>#187: <paramref name="QuestionId"/> is NULL for a pool item, which has no question row;
/// the pinned <paramref name="ItemVersionId"/> is what a re-read addresses.</summary>
public sealed record QuestionOutcomeDto(
    int DisplayOrder,
    int? QuestionId,
    string? CategoryName,
    string Stem,
    string? SelectedOptionLabel,
    string? SelectedOptionText,
    string? CorrectOptionLabel,
    string? CorrectOptionText,
    bool? IsCorrect,
    bool HintUsed,
    string? Explanation,
    string? AudioPath = null,
    int? AudioStartMs = null,
    int? AudioEndMs = null,
    string? Transcript = null,
    string QuestionType = "mcq",
    string? ResponseText = null,
    string? SelectedOptionIdsJson = null,
    string? AcceptedAnswersJson = null,
    int? ItemVersionId = null);
