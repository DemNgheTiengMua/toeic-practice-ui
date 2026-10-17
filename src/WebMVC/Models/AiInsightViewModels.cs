using Contracts;

namespace WebMVC.Models;

public sealed record AiInsightPanelViewModel(
    string Scope,
    int ActivityId,
    AiInsightDto? Insight = null,
    RubricHighlightDto? RubricHighlight = null,
    int? OverallAccuracyPercent = null
);
