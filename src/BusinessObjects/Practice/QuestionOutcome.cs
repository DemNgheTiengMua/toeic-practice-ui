using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

/// <summary>
/// #187: one row of a drill's summary. <see cref="QuestionId"/> is NULL for a pool item; the pinned
/// version then supplies the stem, the options and the explanation, so a re-versioned item cannot
/// change what a finished session's summary shows.
/// </summary>
public sealed record QuestionOutcome(
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
