using System;

namespace BusinessObjects;

public sealed record MockExamItem(
    int ExamId,
    int CertId,
    string CertCode,
    string Name,
    string? Description,
    int DurationMinutes,
    int? QuestionCount,
    string? SourceName,
    int CreditCost,
    int? ActiveAttemptId,
    DateTimeOffset? ActiveAttemptExpiresAt,
    int? CompletedAttemptScore,
    string? CompletedAttemptBand);
