using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record AttemptTakeDto(
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    AttemptStatusDto Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    int TotalQuestions,
    int AnsweredCount,
    int FlaggedCount,
    AttemptQuestionDto CurrentQuestion,
    IReadOnlyList<NavigatorItemDto> Navigator,
    bool IsAutoSubmitted,
    IReadOnlyList<AttemptQuestionDto>? Questions = null,
    IReadOnlyList<AttemptPhaseDto>? Phases = null,
    int? ActiveSkillId = null,
    int ActiveUnitNumber = 1,
    int? ActiveGroupId = null,
    DateTimeOffset? PhaseDeadlineAt = null,
    bool IsSkillLocked = false,
    string? AuthorizationCode = null,
    int LastAudioOffsetMs = 0,
    string? CandidateNotesJson = null);
