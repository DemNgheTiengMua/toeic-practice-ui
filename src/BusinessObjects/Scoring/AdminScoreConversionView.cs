using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record AdminScoreConversionView(
    int CertId,
    string CertCode,
    string CertName,
    IReadOnlyList<CertificateOption> Certificates,
    int? ExamId,
    string? ExamName,
    IReadOnlyList<SkillScoreScaleGroup> SkillScales,
    IReadOnlyList<ScoreBandConfigItem> Bands);
