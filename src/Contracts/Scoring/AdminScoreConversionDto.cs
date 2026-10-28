using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record AdminScoreConversionDto(
    int CertId,
    string CertCode,
    string CertName,
    IReadOnlyList<CertificateOptionDto> Certificates,
    int? ExamId,
    string? ExamName,
    IReadOnlyList<SkillScoreScaleGroupDto> SkillScales,
    IReadOnlyList<ScoreBandConfigDto> Bands);
