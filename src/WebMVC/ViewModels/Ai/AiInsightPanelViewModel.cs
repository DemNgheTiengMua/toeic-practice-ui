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
    public string FormattedNarrative { get; } =
        !string.IsNullOrEmpty(Insight?.FormattedNarrative)
            ? Insight.FormattedNarrative
            : AiNarrativeFormatter.ToSafeHtml(Insight?.Narrative);
    public string Status { get; } = Insight?.Status.ToWireCode() ?? "pending";
    public bool IsDone { get; } = Insight?.Status == AiInsightStatusDto.Done;
    public bool IsFailed { get; } = Insight?.Status == AiInsightStatusDto.Failed;
    public bool IsPending { get; } = Insight?.Status is not (AiInsightStatusDto.Done or AiInsightStatusDto.Failed);
    public string TrendBadge { get; } = Insight?.Trend switch
    {
        AiInsightTrendDto.Improving => "badge--success",
        AiInsightTrendDto.Declining => "badge--danger",
        _ => "badge--warning"
    };
    public string TrendLabel { get; } = Insight?.Trend switch
    {
        AiInsightTrendDto.Improving => "Trend: Improving",
        AiInsightTrendDto.Declining => "Trend: Declining",
        _ => "Trend: Flat"
    };
    public IReadOnlyDictionary<int, string> RecommendationBadges { get; } = Insight?.Recommendations.ToDictionary(
        r => r.RecId, r => r.Severity switch
        {
            RecommendationSeverityDto.Major => "badge--danger",
            RecommendationSeverityDto.Minor => "badge--warning",
            _ => "badge--info"
        }) ?? new Dictionary<int, string>();
    public IReadOnlyDictionary<int, string> RecommendationLabels { get; } = Insight?.Recommendations.ToDictionary(
        r => r.RecId, r => r.Severity switch
        {
            RecommendationSeverityDto.Major => "Priority",
            RecommendationSeverityDto.Minor => "Focus",
            _ => "Tip"
        }) ?? new Dictionary<int, string>();
    public string RubricReviewUrl { get; } = RubricHighlight?.Purpose == ExamPurposeDto.SpeakingPractice
        ? $"/speaking/review/{RubricHighlight.AttemptId}" : $"/writing/review/{RubricHighlight?.AttemptId}";
    public string RubricScoreDisplay { get; } = RubricHighlight?.Score.ToString("0.##") ?? "";
    public string RubricMaxScoreDisplay { get; } = RubricHighlight?.MaxScore.ToString("0.##") ?? "";
}
