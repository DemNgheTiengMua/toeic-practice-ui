using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record SpeakingPracticeInstructionsView(
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
    IReadOnlyList<SpeakingSectionInfo> Sections
);
