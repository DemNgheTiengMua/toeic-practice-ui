using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

/// <summary>
/// #187: one question of a drill. <see cref="QuestionId"/> is NULL for a pool item, which has no
/// <c>Questions</c> row; <see cref="ItemVersionId"/> is then the thing the answer is addressed by, the
/// same identity <c>AttemptAnswers</c> pins.
/// </summary>
public sealed record PracticeQuestionView(
    int SessionId,
    string SectionName,
    int? QuestionId,
    int DisplayOrder,
    int TotalQuestions,
    string Stem,
    string? CategoryName,
    string? Hint,
    bool HintUsed,
    IReadOnlyList<PracticeQuestionOption> Options,
    int? SelectedOptionId,
    bool? IsCorrect,
    int? CorrectOptionId,
    string? Explanation,
    string? Transcript,
    string? Passage,
    string? AudioPath,
    string? ImagePath,
    string? ImageAlt,
    bool IsSaved = false,
    int? AudioStartMs = null,
    int? AudioEndMs = null,
    string QuestionType = "mcq",
    int? MaxSelections = null,
    bool AllowNumber = true,
    string? AcceptedAnswersJson = null,
    string? RequiredKeywords = null,
    int? MinWords = null,
    int? MaxWords = null,
    string? ResponseText = null,
    IReadOnlyList<int>? SelectedOptionIds = null,
    string? SelectedOptionIdsJson = null,
    IReadOnlyList<int>? CorrectOptionIds = null,
    int? ItemVersionId = null,
    int? SavedQuestionId = null);
