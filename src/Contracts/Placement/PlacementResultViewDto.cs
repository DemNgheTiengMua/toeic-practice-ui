using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record PlacementResultViewDto(
    int PlacementId,
    int AttemptId,
    int CertId,
    string CertCode,
    string ExamName,
    DateTimeOffset TakenAt,
    int DurationMinutes,
    int TotalQuestions,
    int TotalScore,
    string ResultBandCode,
    string ResultBandName,
    List<CefrBandInfoDto> CefrBands,
    List<PlacementSkillScoreInfoDto> SkillScores,
    string? WeakerSkillName,
    string? BandNote,
    bool IsAutoSubmitted = false,
    int FreeResponseCount = 0,
    int GradedFreeResponseCount = 0,
    int FailedFreeResponseCount = 0,
    // #152: stored units per reported point; the page divides by it before printing a score.
    int ScoreDisplayFactor = 1,
    bool IsGraded = true,
    // #213 / story 55: the per-skill rows, carrying ReusedFromPrior and the original MeasuredAt.
    List<PlacementSkillResultInfoDto>? SkillResults = null,
    PlacementScopeDto Scope = PlacementScopeDto.AllSkills,
    decimal? ListeningPct = null,
    decimal? ReadingPct = null,
    decimal? SpeakingPct = null,
    decimal? WritingPct = null,
    decimal? OverallPct = null,
    List<CertificateBandPreviewDto>? CertificatePreviews = null);

public sealed record CertificateBandPreviewDto(
    int CertId,
    string CertCode,
    string CertName,
    int SkillCount,
    int MappedScaledScore,
    string MappedBandCode,
    string MappedBandName,
    int ScoreDisplayFactor = 1);
