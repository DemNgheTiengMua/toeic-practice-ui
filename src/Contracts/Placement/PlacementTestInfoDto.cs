using System;

namespace Contracts;

public sealed record PlacementTestInfoDto(
    int PlacementId,
    int AttemptId,
    int ExamId,
    PlacementStatusDto Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    bool IsAutoSubmitted);
