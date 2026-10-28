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
    SpeakingQuestionReviewItem? LowestScoredQuestion
);
