using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

public sealed record QuestionOutcome(
    int DisplayOrder,
    int QuestionId,
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
    string? AcceptedAnswersJson = null);
