using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record AttemptResumeView(
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
    IReadOnlyList<AttemptAnswerSheetItem> AnswerSheet);
