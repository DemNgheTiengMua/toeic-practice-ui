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

    /// <summary>
    /// #213 / story 55: the per-skill rows, in the certificate's order, each with whether it was carried
    /// over and the date it was actually measured. A page-level "some of this is reused" banner would be
    /// the wrong grain: a sitting can measure one skill and carry another, and the learner needs to know
    /// which number is which.
    /// </summary>
    public IReadOnlyList<PlacementSkillResultInfoDto> SkillResults { get; init; } = [];

    /// <summary>
    /// The skill's measurement date in the learner's own zone. <b>The words matter:</b> a carried result
    /// prints "measured" against the earlier date, never "taken", so it cannot be confused with the
    /// sitting's date at the top of the page.
    /// </summary>
    public string MeasuredOn(PlacementSkillResultInfoDto skill) =>
        skill.MeasuredAt is { } at ? Dates.FormatCustom(at, "d MMMM yyyy") : string.Empty;

    /// <summary>The one carry-over sentence, so the badge and its date are never separated.</summary>
    public string CarriedOverLabel(PlacementSkillResultInfoDto skill) =>
        $"Carried over from your sitting on {MeasuredOn(skill)}";

    /// <summary>`B2 (78/100)` — a level and a position, never a band and never a bare percentage.</summary>
    public string LevelAndPosition(PlacementSkillResultInfoDto skill) =>
        skill is { LevelCode: { } level, PositionInBand: { } position }
            ? $"{level} ({position}/100)"
            : "Not measured";

    /// <summary>The formatter, injected so this type stays a pure projection of the DTO.</summary>
    public WebMVC.Formatters.DateTimeFormatter Dates { get; init; } = null!;

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
