using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

public sealed record GradedPracticeAnswerResult(
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
    string? SelectedOptionIdsJson = null);
