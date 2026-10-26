using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record ExamHistoryItemDto(
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
    IReadOnlyList<AttemptSkillScoreDto> SkillScores,
    AttemptStatusDto Status);
