using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record AttemptResultView(
    int AttemptId,
    int ExamId,
    string ExamName,
    int CertId,
    string CertCode,
    string CertName,
    AttemptStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset SubmittedAt,
    int DurationSeconds,
    bool IsAutoSubmitted,
    int TotalScore,
    string? BandCode,
    string? BandName,
    IReadOnlyList<AttemptSkillScoreItem> SkillScores,
    int? PreviousAttemptScore,
    int? ScoreDelta,
    int FreeResponseCount = 0,
    int GradedFreeResponseCount = 0,
    int FailedFreeResponseCount = 0);
