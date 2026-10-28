using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

public record AiInsightResult(
    bool Success,
    string? FailureReason,
    string? Model,
    string? Narrative,
    AiInsightTrend? Trend,
    IReadOnlyList<GeneratedRecommendation> Recommendations,
    int? PromptTokens = null,
    int? CompletionTokens = null
);
