using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record ExamHistoryItem(
    int AttemptId,
    int ExamId,
    string ExamName,
    int CertId,
    string CertCode,
    DateTimeOffset StartedAt,
    DateTimeOffset SubmittedAt,
    int TotalScore,
    string? BandCode,
    string? BandName,
    IReadOnlyList<AttemptSkillScoreItem> SkillScores,
    AttemptStatus Status);
