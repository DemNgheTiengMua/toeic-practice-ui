using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record AttemptTakeView(
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    AttemptStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    int TotalQuestions,
    int AnsweredCount,
    int FlaggedCount,
    AttemptQuestionInfo CurrentQuestion,
    IReadOnlyList<NavigatorItem> Navigator,
    bool IsAutoSubmitted,
    IReadOnlyList<AttemptQuestionInfo>? Questions = null,
    IReadOnlyList<AttemptPhaseInfo>? Phases = null,
    int? ActiveSkillId = null,
    int ActiveUnitNumber = 1,
    int? ActiveGroupId = null,
    DateTimeOffset? PhaseDeadlineAt = null,
    bool IsSkillLocked = false,
    string? AuthorizationCode = null,
    int LastAudioOffsetMs = 0,
    string? CandidateNotesJson = null);
