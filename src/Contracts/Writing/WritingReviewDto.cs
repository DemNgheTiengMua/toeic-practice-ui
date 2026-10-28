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
    IReadOnlyList<WritingQuestionReviewItemDto> Questions
);

