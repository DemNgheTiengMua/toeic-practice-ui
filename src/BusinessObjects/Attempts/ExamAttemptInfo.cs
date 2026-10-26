using System;

namespace BusinessObjects;

public sealed record ExamAttemptInfo(
    int AttemptId,
    int ExamId,
    AttemptStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    bool IsAutoSubmitted);
