using Contracts;

namespace WebMVC.Models;

public sealed record PlacementResultViewModel(
    PlacementResultViewDto Result,
    string? ErrorMessage = null,
    AiInsightDto? Insight = null)
{
    public string TakenAtDisplay { get; init; } = string.Empty;
    public bool IsAwaitingGrading { get; init; }
    public string TotalScoreDisplay { get; init; } = string.Empty;
    public string ScoreSummary { get; init; } = string.Empty;
    public int MaximumScore { get; init; }
    public PlacementSkillScoreInfoDto? Listening { get; init; }
    public PlacementSkillScoreInfoDto? Reading { get; init; }
    public IReadOnlyList<AttemptBandViewModel> Bands { get; init; } = [];
    public AiInsightPanelViewModel InsightPanel { get; init; } = new("attempt", Result.AttemptId, Insight);
}
