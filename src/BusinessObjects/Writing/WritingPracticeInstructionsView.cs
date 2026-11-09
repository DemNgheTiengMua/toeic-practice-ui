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
)
{
    /// <summary>
    /// #239: every attempt this learner has already made on this exam, newest first, so a prior
    /// submission is listed and re-openable. Empty for a learner who has not started it. The active
    /// attempt appears here too — it is one of the priors, and listing it in both places would give the
    /// same attempt two rows and two routes.
    /// </summary>
    public IReadOnlyList<PriorAttempt> Attempts { get; init; } = [];
}
