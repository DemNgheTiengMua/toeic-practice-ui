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

public sealed record SectionScoreInputDto(
    int? SectionId,
    string? SectionCode,
    int? SkillId,
    decimal RawScore,
    decimal ScoreWeight = 1.0m,
    int? MaxQuestions = null);

public sealed record SkillScoreInputDto(
    int? SkillId,
    string? SkillCode,
    decimal? RawScore,
    IReadOnlyList<SectionScoreInputDto>? Sections = null);

public sealed record ScorePreviewRequestDto(
    int? CertId,
    string? CertCode,
    string ScoreAggregationRule,
    IReadOnlyList<SkillScoreInputDto> Skills,
    IReadOnlyList<UpdateScoreScaleEntryDto>? CustomScales = null,
    IReadOnlyList<UpdateScoreBandEntryDto>? CustomBands = null);

public sealed record SectionScorePreviewResultDto(
    int? SectionId,
    string? SectionCode,
    decimal RawScore,
    decimal ScoreWeight,
    decimal ScaledScore);

public sealed record SkillScorePreviewResultDto(
    int? SkillId,
    string? SkillCode,
    decimal RawScore,
    int ScaledScore,
    IReadOnlyList<SectionScorePreviewResultDto>? Sections = null);

public sealed record ScorePreviewResultDto(
    string ScoreAggregationRule,
    IReadOnlyList<SkillScorePreviewResultDto> SkillScores,
    int TotalScore,
    string? BandCode,
    string? BandName);
