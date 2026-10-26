using System;

namespace BusinessObjects;

public sealed record PlacementTestInfo(
    int PlacementId,
    int AttemptId,
    int ExamId,
    PlacementStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    bool IsAutoSubmitted);
