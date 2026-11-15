using System.Collections.Generic;

namespace Contracts;

public sealed record AttemptReviewQuestionDto(
    int QuestionId,
    int DisplayOrder,
    int TotalQuestions,
    int SectionId,
    string SectionCode,
    string SectionName,
    string SkillCode,
    string SkillName,
    ModalityDto Modality,
    string Stem,
    string? ImagePath,
    string? ImageAlt,
    string? AudioPath,
    int? AudioStartMs,
    int? AudioEndMs,
    string? Transcript,
    string? Passage,
    string? Explanation,
    IReadOnlyList<AttemptReviewOptionDto> Options,
    int? SelectedOptionId,
    bool? IsCorrect,
    bool IsFlagged,
    // #375: the free-response half of a review, so a placement review shows the written answer with
    // its rubric marks and the spoken answer with its transcript. Null on an objective position.
    string? ResponseText = null,
    string? ResponseAudioPath = null,
    decimal? AiScore = null,
    string? AiFeedback = null,
    // #385: a refused grading carries no score, so the review must carry its reason.
    string? AiFailureReason = null,
    IReadOnlyList<AttemptReviewRubricScoreDto>? RubricScores = null);
