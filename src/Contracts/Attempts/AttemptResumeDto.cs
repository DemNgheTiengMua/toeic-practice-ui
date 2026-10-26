using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record AttemptResumeDto(
    int AttemptId,
    int ExamId,
    string AuthorizationCode,
    int? ActiveSkillId,
    int ActiveUnitNumber,
    int? ActiveGroupId,
    int? LastQuestionId,
    int LastAudioOffsetMs,
    string? CandidateNotesJson,
    int RemainingSeconds,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? PhaseDeadlineAt,
    IReadOnlyList<AttemptAnswerSheetItemDto> AnswerSheet);
