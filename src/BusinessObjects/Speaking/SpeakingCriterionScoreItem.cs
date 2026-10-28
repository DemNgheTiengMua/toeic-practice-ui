using System;
using System.Collections.Generic;

namespace BusinessObjects;

public record SpeakingCriterionScoreItem(
    int CriterionId,
    string Code,
    string Name,
    decimal Score,
    decimal MaxScore,
    string? Comment
);
