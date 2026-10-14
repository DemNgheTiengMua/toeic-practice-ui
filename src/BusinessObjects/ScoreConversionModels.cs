using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record ScoreScaleRowItem(
    int? ScaleId,
    int SkillId,
    string SkillCode,
    string SkillName,
    int RawScore,
    int ScaledScore);

public sealed record SkillScoreScaleGroup(
    int SkillId,
    string SkillCode,
    string SkillName,
    int MaxQuestions,
    IReadOnlyList<ScoreScaleRowItem> Scales);

public sealed record ScoreBandConfigItem(
    int BandId,
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal,
    int DisplayOrder);

public sealed record AdminScoreConversionView(
    int CertId,
    string CertCode,
    string CertName,
    IReadOnlyList<CertificateOption> Certificates,
    int? ExamId,
    string? ExamName,
    IReadOnlyList<SkillScoreScaleGroup> SkillScales,
    IReadOnlyList<ScoreBandConfigItem> Bands);

public sealed record UpdateScoreScaleEntry(
    int SkillId,
    int RawScore,
    int ScaledScore);

public sealed record UpdateScoreBandEntry(
    int? BandId,
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal,
    int DisplayOrder);

public sealed record UpdateScoreConversionCommand(
    int? ExamId,
    IReadOnlyList<UpdateScoreScaleEntry> Scales,
    IReadOnlyList<UpdateScoreBandEntry> Bands);
