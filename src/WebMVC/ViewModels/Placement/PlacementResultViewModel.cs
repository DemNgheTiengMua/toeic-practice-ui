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
    public string MaximumScore { get; init; } = string.Empty;
    public IReadOnlyList<PlacementSkillScoreInfoDto> Skills { get; init; } = [];
    public IReadOnlyList<AttemptBandViewModel> Bands { get; init; } = [];
    public AiInsightPanelViewModel InsightPanel { get; init; } = new("attempt", Result.AttemptId, Insight);

    /// <summary>#152: a skill's stored score on the certificate's own unit, ready to print.</summary>
    public string SkillScoreDisplay(PlacementSkillScoreInfoDto skill) =>
        ScoreDisplay.Format(skill.ScaledScore, Result.ScoreDisplayFactor);
}
