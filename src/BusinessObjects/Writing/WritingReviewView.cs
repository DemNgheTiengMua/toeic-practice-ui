using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record WritingReviewView(
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    DateTimeOffset? SubmittedAt,
    int TotalQuestions,
    int AnsweredQuestions,
    decimal TotalScore,
    decimal MaxScore,
    IReadOnlyList<WritingQuestionReviewItem> Questions,
    // E1.3 (#148): the band this writing's rubric lands in, on this certificate's own bands. An
    // estimate from one skill, never the certificate's formal result.
    string? EstimatedBandCode = null,
    string? EstimatedBandName = null
);
