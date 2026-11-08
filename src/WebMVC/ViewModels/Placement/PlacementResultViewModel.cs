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

    /// <summary>
    /// #217 / PLC-19: why a skill is `not_measured`, in the learner's words. The row still exists and is
    /// shown - a partial result that silently omitted the skill would look complete, which is the
    /// "partial result that looks complete" PLC-19 refuses.
    /// </summary>
    public static string NotMeasuredReason(string? failureReason) => failureReason switch
    {
        "candidate_skipped" => "You did not attempt this skill.",
        "inactivity_timeout" => "The sitting ended before this skill was reached.",
        "ai_grading_failed" => "The grader could not score this task.",
        _ => "Nothing was measured for this skill.",
    };

    /// <summary>A skill still in the AI queue - a wait, not a failure.</summary>
    public static bool IsGrading(PlacementSkillResultInfoDto skill) => skill.Status == "grading";

    /// <summary>#217 / PLC-35: the retry is offered exactly when some task's grading failed.</summary>
    public bool CanRetryGrading =>
        SkillResults.Any(skill => skill.Status == "not_measured" && skill.FailureReason == "ai_grading_failed");

    /// <summary>#217 / PLC-19: `done` with a reason and no measurement, i.e. nothing could be measured.</summary>
    public bool HasNoResult => Result.SkillResults is { Count: > 0 }
        && SkillResults.All(skill => skill.Status != "measured" && skill.Status != "estimated_from_one_task");

    public string RetryUrl { get; init; } = string.Empty;

    /// <summary>`B2 (78/100)` — a level and a position, never a band and never a bare percentage.</summary>
    public string LevelAndPosition(PlacementSkillResultInfoDto skill) =>
        skill is { LevelCode: { } level, PositionInBand: { } position }
            ? $"{level} ({position}/100)"
            : "Not measured";

    /// <summary>
    /// #217 / PLC-19: what the per-skill table prints in the Level column. A `not_measured` or `grading`
    /// row says so in words and never prints a level - a zero or "A1" here is the fabricated bottom band
    /// the ticket forbids (PLC-35's "neither zero nor A1").
    /// </summary>
    public string LevelCell(PlacementSkillResultInfoDto skill) => skill.Status switch
    {
        "grading" => "Grading",
        "not_measured" => "Not measured",
        "exempt_accommodated" => "Exempt",
        _ => LevelAndPosition(skill),
    };

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
