using System.Collections.Generic;

namespace Contracts;

public sealed record MockExamInstructionsDto(
    int ExamId,
    int CertId,
    string CertCode,
    string ExamName,
    string? Description,
    int DurationMinutes,
    int? QuestionCount,
    int CreditCost,
    int CreditBalance,
    int? ActiveAttemptId,
    IReadOnlyList<MockExamSectionInfoDto> Sections);
