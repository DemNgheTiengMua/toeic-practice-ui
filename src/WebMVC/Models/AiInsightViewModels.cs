using Contracts;
using WebMVC.Formatters;

namespace WebMVC.Models;

public sealed record AiInsightPanelViewModel(
    string Scope,
    int ActivityId,
    AiInsightDto? Insight = null,
    RubricHighlightDto? RubricHighlight = null,
    int? OverallAccuracyPercent = null
)
{
    public string FormattedNarrative =>
        !string.IsNullOrEmpty(Insight?.FormattedNarrative)
            ? Insight.FormattedNarrative
            : AiNarrativeFormatter.ToSafeHtml(Insight?.Narrative);
}
