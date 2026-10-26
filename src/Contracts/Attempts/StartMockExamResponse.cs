using System;

namespace Contracts;

public sealed record StartMockExamResponse(
    int AttemptId,
    int ExamId,
    AttemptStatusDto Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    bool IsAutoSubmitted);
