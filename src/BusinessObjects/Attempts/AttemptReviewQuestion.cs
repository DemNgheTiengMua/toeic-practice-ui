using System.Collections.Generic;

namespace BusinessObjects;

public sealed record AttemptReviewQuestion(
    int QuestionId,
    int DisplayOrder,
    int TotalQuestions,
    int SectionId,
    string SectionCode,
    string SectionName,
    string SkillCode,
    string SkillName,
    Modality Modality,
    string Stem,
    string? ImagePath,
    string? ImageAlt,
    string? AudioPath,
    int? AudioStartMs,
    int? AudioEndMs,
    string? Transcript,
    string? Passage,
    string? Explanation,
    IReadOnlyList<AttemptReviewOption> Options,
    int? SelectedOptionId,
    bool? IsCorrect,
    bool IsFlagged);
