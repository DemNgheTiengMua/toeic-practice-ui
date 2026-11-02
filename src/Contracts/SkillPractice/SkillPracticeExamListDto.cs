using System;

namespace Contracts;

/// <summary>#159: the door for a free-response skill. Writing and Speaking are graded by AI and are
/// not part of the typed drill, so each has its own list of published practice exams to open.</summary>
public sealed record SkillPracticeExamListDto(
    CertificateOptionDto Certificate,
    int CreditBalance,
    IReadOnlyList<SkillPracticeExamDto> Exams);

public sealed record SkillPracticeExamDto(
    int ExamId,
    string Name,
    string? Description,
    int DurationMinutes,
    int CreditCost,
    int? ActiveAttemptId,
    int? LatestGradedAttemptId,
    DateTimeOffset? LatestGradedAt);
