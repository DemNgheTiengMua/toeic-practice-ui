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

public sealed record SectionScoreInput(
    int? SectionId,
    string? SectionCode,
    int? SkillId,
    decimal RawScore,
    decimal ScoreWeight = 1.0m,
    int? MaxQuestions = null);

public sealed record SkillScoreInput(
    int? SkillId,
    string? SkillCode,
    decimal? RawScore,
    IReadOnlyList<SectionScoreInput>? Sections = null);

public sealed record ScorePreviewRequest(
    int? CertId,
    string? CertCode,
    string ScoreAggregationRule,
    IReadOnlyList<SkillScoreInput> Skills,
    IReadOnlyList<UpdateScoreScaleEntry>? CustomScales = null,
    IReadOnlyList<UpdateScoreBandEntry>? CustomBands = null);

public sealed record SectionScorePreviewResult(
    int? SectionId,
    string? SectionCode,
    decimal RawScore,
    decimal ScoreWeight,
    decimal ScaledScore);

public sealed record SkillScorePreviewResult(
    int? SkillId,
    string? SkillCode,
    decimal RawScore,
    int ScaledScore,
    IReadOnlyList<SectionScorePreviewResult>? Sections = null);

public sealed record ScorePreviewResult(
    string ScoreAggregationRule,
    IReadOnlyList<SkillScorePreviewResult> SkillScores,
    int TotalScore,
    string? BandCode,
    string? BandName);
