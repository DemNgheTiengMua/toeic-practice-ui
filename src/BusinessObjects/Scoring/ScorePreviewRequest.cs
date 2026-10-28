using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record ScorePreviewRequest(
    int? CertId,
    string? CertCode,
    string ScoreAggregationRule,
    IReadOnlyList<SkillScoreInput> Skills,
    IReadOnlyList<UpdateScoreScaleEntry>? CustomScales = null,
    IReadOnlyList<UpdateScoreBandEntry>? CustomBands = null);
