using System;
using System.Collections.Generic;

namespace Contracts;

/// <summary>#216 / PLC-14: the resume screen's facts. A read - nothing here is written by asking.</summary>
public sealed record PlacementResumeViewDto(
    int PlacementId,
    int AttemptId,
    string ExamName,
    string CertCode,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<PlacementResumeSkillDto> Skills,
    int? ActiveSkillId,
    string? ActiveSkillName,
    int? ActiveStage,
    int TotalStages,
    int SyncedAnswerCount,
    int TotalQuestions,
    int? LastQuestionId);

public sealed record PlacementResumeSkillDto(
    int SkillId,
    string Code,
    string Name,
    bool IsDone,
    bool IsActive,
    int AnsweredCount,
    int TotalQuestions);

/// <summary>#216 / PLC-15: one interrupted recording, reported with the token that makes a reload the
/// same interruption. `Outcome` is <c>retry</c>, <c>finalise</c> or <c>replace_with_sibling</c>.</summary>
public sealed record RecordingInterruptionRequest(
    int QuestionId,
    string SessionToken,
    int CapturedMs);

public sealed record RecordingDecisionDto(
    int Interruptions,
    string Outcome,
    int? ReplacementQuestionId);
