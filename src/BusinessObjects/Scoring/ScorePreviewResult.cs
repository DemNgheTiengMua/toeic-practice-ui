using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record ScorePreviewResult(
    string ScoreAggregationRule,
    IReadOnlyList<SkillScorePreviewResult> SkillScores,
    int TotalScore,
    string? BandCode,
    string? BandName);
