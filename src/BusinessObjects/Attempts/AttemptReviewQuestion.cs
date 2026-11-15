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
    bool IsFlagged,
    // #375: a free response has no options, so the review reads its content from here. Null on an
    // objective position, which is the only way the page can tell the two apart.
    string? ResponseText = null,
    string? ResponseAudioPath = null,
    decimal? AiScore = null,
    string? AiFeedback = null,
    // #385: a refused grading carries no score, so the review must carry its reason - otherwise a
    // learner sees an unexplained blank where a mark should be, which is the defect #376 fixed on the
    // result page and this is the same fact one screen further in.
    string? AiFailureReason = null,
    IReadOnlyList<AttemptReviewRubricScore>? RubricScores = null);
