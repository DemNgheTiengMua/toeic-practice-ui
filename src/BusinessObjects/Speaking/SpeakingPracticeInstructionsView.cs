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
)
{
    /// <summary>
    /// #239: every attempt this learner has already made on this exam, newest first, so a prior take is
    /// listed and re-openable. Empty for a learner who has not started it.
    /// </summary>
    public IReadOnlyList<PriorAttempt> Attempts { get; init; } = [];
}
