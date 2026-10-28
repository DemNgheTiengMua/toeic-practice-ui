using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record ScorePreviewRequestDto(
    int? CertId,
    string? CertCode,
    string ScoreAggregationRule,
    IReadOnlyList<SkillScoreInputDto> Skills,
    IReadOnlyList<UpdateScoreScaleEntryDto>? CustomScales = null,
    IReadOnlyList<UpdateScoreBandEntryDto>? CustomBands = null);
