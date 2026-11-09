using System;
using System.Collections.Generic;

namespace Contracts;

public record WritingReviewDto(
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    DateTimeOffset? SubmittedAt,
    int TotalQuestions,
    int AnsweredQuestions,
    decimal TotalScore,
    decimal MaxScore,
    IReadOnlyList<WritingQuestionReviewItemDto> Questions,
    string? EstimatedBandCode = null,
    string? EstimatedBandName = null,
    // #239: how many tasks carry a rubric score. The page labels a band as an estimate from one task
    // when this is 1.
    int GradedQuestions = 0
);
