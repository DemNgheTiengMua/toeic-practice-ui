using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record WritingTakeView(
    int AttemptId,
    int ExamId,
    string ExamName,
    int CertId,
    string CertCode,
    AttemptStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    int CurrentQuestionId,
    int CurrentQuestionIndex,
    int TotalQuestions,
    WritingQuestionView Question,
    IReadOnlyList<WritingQuestionNav> Questions
);
