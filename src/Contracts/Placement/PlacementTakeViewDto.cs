using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record PlacementTakeViewDto(
    int PlacementId,
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    PlacementStatusDto Status,
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
    IReadOnlyList<AttemptPhaseDto>? Phases = null);
