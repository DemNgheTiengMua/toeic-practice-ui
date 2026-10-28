using System;
using System.Collections.Generic;

namespace Contracts;

public record SpeakingTakeDto(
    int AttemptId,
    int ExamId,
    string ExamName,
    int CertId,
    string CertCode,
    AttemptStatusDto Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    int CurrentQuestionId,
    int CurrentQuestionIndex,
    int TotalQuestions,
    int SavedCount,
    SpeakingQuestionDto Question,
    IReadOnlyList<SpeakingQuestionNavDto> Questions
);

