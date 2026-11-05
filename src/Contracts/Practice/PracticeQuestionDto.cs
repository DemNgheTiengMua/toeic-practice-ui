namespace Contracts;

/// <summary>#187: <paramref name="QuestionId"/> is NULL for a pool item, which is addressed by
/// <paramref name="ItemVersionId"/> instead.</summary>
public sealed record PracticeQuestionDto(
    int SessionId,
    string SectionName,
    int? QuestionId,
    int DisplayOrder,
    int TotalQuestions,
    string Stem,
    string? CategoryName,
    string? Hint,
    bool HintUsed,
    IReadOnlyList<PracticeOptionDto> Options,
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
    int? ItemVersionId = null)
{
    /// <summary>
    /// #187: the id the browser addresses this question by. A section drill addresses its question
    /// row; a pool item has none, so its pinned version is the address instead. One opaque handle per
    /// question keeps the navigator, the answer POST and the hint POST from each inventing a second
    /// identity — and the service resolves either one.
    /// </summary>
    public int Address => QuestionId ?? ItemVersionId ?? 0;
}
