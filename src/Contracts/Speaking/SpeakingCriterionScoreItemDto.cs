using System;
using System.Collections.Generic;

namespace Contracts;

public record SpeakingCriterionScoreItemDto(
    int CriterionId,
    string Code,
    string Name,
    decimal Score,
    decimal MaxScore,
    string? Comment
);
