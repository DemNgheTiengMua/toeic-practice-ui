using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record AttemptResultDto(
    int AttemptId,
    int ExamId,
    string ExamName,
    int CertId,
    string CertCode,
    string CertName,
    AttemptStatusDto Status,
    DateTimeOffset StartedAt,
    DateTimeOffset SubmittedAt,
    int DurationSeconds,
    bool IsAutoSubmitted,
    int TotalScore,
    string? BandCode,
    string? BandName,
    IReadOnlyList<AttemptSkillScoreDto> SkillScores,
    int? PreviousAttemptScore,
    int? ScoreDelta,
    int FreeResponseCount = 0,
    int GradedFreeResponseCount = 0,
    int FailedFreeResponseCount = 0);
