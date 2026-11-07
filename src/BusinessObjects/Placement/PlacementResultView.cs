using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record PlacementResultView(
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
    List<CefrBandInfo> CefrBands,
    List<PlacementSkillScoreInfo> SkillScores,
    string? WeakerSkillName,
    string? BandNote,
    bool IsAutoSubmitted = false,
    int FreeResponseCount = 0,
    int GradedFreeResponseCount = 0,
    int FailedFreeResponseCount = 0,
    // #152: stored units per reported point; the page divides by it before printing a score.
    int ScoreDisplayFactor = 1,
    bool IsGraded = true,
    // #213 / story 55: the per-skill rows, so the result screen can badge a carried-over number and
    // print the date it was actually measured. Defaulted so existing construction sites compile.
    IReadOnlyList<PlacementSkillResultInfo>? SkillResults = null);
