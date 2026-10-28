using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Services;

public record AiGradingResult(
    bool Success,
    string? FailureReason,
    string? Model,
    IReadOnlyList<AiCriterionScoreResult> Scores,
    string? Transcript = null
);
