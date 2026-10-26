using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record PlacementTakeView(
    int PlacementId,
    int AttemptId,
    int ExamId,
    string ExamName,
    string CertCode,
    PlacementStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    int RemainingSeconds,
    int TotalQuestions,
    int AnsweredCount,
    int FlaggedCount,
    AttemptQuestionInfo CurrentQuestion,
    List<NavigatorItem> Navigator,
    bool IsAutoSubmitted,
    IReadOnlyList<AttemptQuestionInfo>? Questions = null,
    IReadOnlyList<AttemptPhaseInfo>? Phases = null);
