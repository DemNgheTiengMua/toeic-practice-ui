using System;
using System.Collections.Generic;

namespace Contracts;

public record SpeakingReviewDto(
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    DateTimeOffset? SubmittedAt,
    int TotalQuestions,
    int AnsweredQuestions,
    decimal TotalScore,
    decimal MaxScore,
    int? ScaledScore,
    IReadOnlyList<SpeakingQuestionReviewItemDto> Questions,
    SpeakingQuestionReviewItemDto? LowestScoredQuestion,
    string? EstimatedBandCode = null,
    string? EstimatedBandName = null,
    int? ScaledScoreMax = null,
    // #152: stored units per reported point; the page divides by it before printing a score.
    int ScoreDisplayFactor = 1,
    // #239: how many tasks carry a rubric score; 1 makes the band an estimate from one task.
    int GradedQuestions = 0
);
