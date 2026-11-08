namespace Contracts;

public sealed record GradedAnswerDto(
    bool? IsCorrect,
    int? SelectedOptionId,
    int? CorrectOptionId,
    string? Explanation,
    string? Transcript,
    bool IsSessionFinished,
    int? NextQuestionDisplayOrder,
    bool IsSaved = false,
    string? AcceptedAnswersJson = null,
    IReadOnlyList<int>? CorrectOptionIds = null,
    string? ResponseText = null,
    string? SelectedOptionIdsJson = null,
    int? SavedQuestionId = null,
    /// <summary>
    /// #238 / U18: the drilled cell's level, present only now that the answer has locked — this reply
    /// <i>is</i> the lock. Null for a section drill, which names no cell.
    /// </summary>
    string? CefrLevelCode = null);
