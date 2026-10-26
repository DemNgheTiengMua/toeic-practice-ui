using System;

namespace Contracts;

public sealed record AttemptTransitionResponse(
    int AttemptId,
    int? ActiveSkillId,
    int ActiveUnitNumber,
    int? ActiveGroupId,
    DateTimeOffset? PhaseDeadlineAt,
    int RemainingSeconds,
    AttemptStatusDto Status,
    bool IsSubmitted);
