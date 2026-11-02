using System;

namespace BusinessObjects;

/// <summary>#159: the published practice exams of one free-response skill, and what the learner has
/// already done with each. Writing and Speaking are answered by AI grading, not by the typed drill,
/// so without this list the feature has no door on the learner's side.
/// </summary>
public sealed record SkillPracticeExamList(
    CertificateOption Certificate,
    int CreditBalance,
    IReadOnlyList<SkillPracticeExamItem> Exams);

/// <param name="ActiveAttemptId">An unfinished attempt to resume; resuming never charges again.</param>
/// <param name="LatestGradedAttemptId">The most recent graded attempt, whose review holds the rubric
/// feedback. Null means the skill has never been graded for this certificate.</param>
public sealed record SkillPracticeExamItem(
    int ExamId,
    string Name,
    string? Description,
    int DurationMinutes,
    int CreditCost,
    int? ActiveAttemptId,
    int? LatestGradedAttemptId,
    DateTimeOffset? LatestGradedAt);
