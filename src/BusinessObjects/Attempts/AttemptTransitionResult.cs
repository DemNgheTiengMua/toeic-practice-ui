using System;

namespace BusinessObjects;

public sealed record AttemptTransitionResult(
    int AttemptId,
    int? ActiveSkillId,
    int ActiveUnitNumber,
    int? ActiveGroupId,
    DateTimeOffset? PhaseDeadlineAt,
    int RemainingSeconds,
    AttemptStatus Status);
