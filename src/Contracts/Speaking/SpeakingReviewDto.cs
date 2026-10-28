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
    SpeakingQuestionReviewItemDto? LowestScoredQuestion
);

