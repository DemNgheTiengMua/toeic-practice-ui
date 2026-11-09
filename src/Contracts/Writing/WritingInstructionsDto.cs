using System;
using System.Collections.Generic;

namespace Contracts;

public record WritingInstructionsDto(
    int ExamId,
    int CertId,
    string CertCode,
    string ExamName,
    string? Description,
    int DurationMinutes,
    int QuestionCount,
    int CreditCost,
    int CreditBalance,
    int? ActiveAttemptId,
    IReadOnlyList<WritingSectionInfoDto> Sections,
    // #239: every attempt already made on this exam, newest first.
    IReadOnlyList<PriorAttemptDto>? Attempts = null
);
