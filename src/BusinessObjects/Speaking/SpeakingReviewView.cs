using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record SpeakingReviewView(
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
    IReadOnlyList<SpeakingQuestionReviewItem> Questions,
    SpeakingQuestionReviewItem? LowestScoredQuestion,
    // E1.3 (#148): the band this speaking rubric lands in, on this certificate's own bands.
    string? EstimatedBandCode = null,
    string? EstimatedBandName = null
);
