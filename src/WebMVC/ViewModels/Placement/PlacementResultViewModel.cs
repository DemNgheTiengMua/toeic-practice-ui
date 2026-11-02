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

    /// <summary>#161: empty when the certificate declares no band range at all. The page says "no scale
    /// configured" rather than printing "out of 990", a TOEIC ceiling no other certificate promises.</summary>
    public string MaximumScore { get; init; } = string.Empty;

    public IReadOnlyList<PlacementSkillScoreInfoDto> Skills { get; init; } = [];

    /// <summary>#161: the strip's columns come from the certificate's own bands, in score order.</summary>
    public BandStripViewModel BandStrip => new(
        Result.CefrBands.OrderBy(b => b.MinTotal)
            .Select(b => new BandStripItemViewModel(b.Code, b.MinTotal, b.MaxTotal,
                "cefr__band" + (b.IsCurrent ? " is-current" : b.IsReached ? " is-reached" : "")))
            .ToList(),
        $"Current band {Result.ResultBandCode}, score {Result.TotalScore}");

    public AiInsightPanelViewModel InsightPanel { get; init; } = new("attempt", Result.AttemptId, Insight);

    /// <summary>#152: a skill's stored score on the certificate's own unit, ready to print.</summary>
    public string SkillScoreDisplay(PlacementSkillScoreInfoDto skill) =>
        ScoreDisplay.Format(skill.ScaledScore, Result.ScoreDisplayFactor);
}
