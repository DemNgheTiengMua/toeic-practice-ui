using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record ScoreScaleRowDto(
    int? ScaleId,
    int SkillId,
    string SkillCode,
    string SkillName,
    int RawScore,
    int ScaledScore);

public sealed record SkillScoreScaleGroupDto(
    int SkillId,
    string SkillCode,
    string SkillName,
    int MaxQuestions,
    IReadOnlyList<ScoreScaleRowDto> Scales);

public sealed record ScoreBandConfigDto(
    int BandId,
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal,
    int DisplayOrder);

public sealed record AdminScoreConversionDto(
    int CertId,
    string CertCode,
    string CertName,
    IReadOnlyList<CertificateOptionDto> Certificates,
    int? ExamId,
    string? ExamName,
    IReadOnlyList<SkillScoreScaleGroupDto> SkillScales,
    IReadOnlyList<ScoreBandConfigDto> Bands);

public sealed record UpdateScoreScaleEntryDto(
    int SkillId,
    int RawScore,
    int ScaledScore);

public sealed record UpdateScoreBandEntryDto(
    int? BandId,
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal,
    int DisplayOrder);

public sealed record UpdateScoreConversionRequest(
    int? ExamId,
    IReadOnlyList<UpdateScoreScaleEntryDto> Scales,
    IReadOnlyList<UpdateScoreBandEntryDto> Bands);
