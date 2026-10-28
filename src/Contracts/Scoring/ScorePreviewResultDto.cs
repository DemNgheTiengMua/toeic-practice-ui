using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record ScorePreviewResultDto(
    string ScoreAggregationRule,
    IReadOnlyList<SkillScorePreviewResultDto> SkillScores,
    int TotalScore,
    string? BandCode,
    string? BandName);
