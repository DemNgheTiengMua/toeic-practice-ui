using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record WritingPracticeInstructionsView(
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
    IReadOnlyList<WritingSectionInfo> Sections
);
