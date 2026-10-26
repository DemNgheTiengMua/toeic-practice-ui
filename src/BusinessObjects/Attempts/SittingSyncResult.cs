using System;

namespace BusinessObjects;

public sealed record SittingSyncResult(
    int AttemptId,
    int RemainingSeconds,
    bool IsAutoSubmitted,
    int? ActiveSkillId,
    int ActiveUnitNumber,
    int? ActiveGroupId,
    DateTimeOffset? PhaseDeadlineAt,
    int LastAudioOffsetMs,
    int AnswersSavedCount);
