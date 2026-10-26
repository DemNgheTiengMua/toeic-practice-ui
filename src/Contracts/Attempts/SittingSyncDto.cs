using System;

namespace Contracts;

public sealed record SittingSyncDto(
    int AttemptId,
    int RemainingSeconds,
    bool IsAutoSubmitted,
    int? ActiveSkillId,
    int ActiveUnitNumber,
    int? ActiveGroupId,
    DateTimeOffset? PhaseDeadlineAt,
    int LastAudioOffsetMs,
    int AnswersSavedCount);
