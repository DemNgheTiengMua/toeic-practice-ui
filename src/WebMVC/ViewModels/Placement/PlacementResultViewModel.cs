using Contracts;
using Microsoft.Extensions.Logging;
using WebMVC.Formatters;

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

    /// <summary>#218: the certificate's own bands, left to right — the one axis every skill row shares.</summary>
    public IReadOnlyList<PlacementBandColumnViewModel> Bands { get; init; } = [];

    /// <summary>#218: one row per examined skill, on that shared axis.</summary>
    public IReadOnlyList<PlacementSkillRowViewModel> Rows { get; init; } = [];

    /// <summary>The measured rows only, for the provenance strip. Prepared here so the view filters nothing.</summary>
    public IReadOnlyList<PlacementSkillRowViewModel> MeasuredRows { get; init; } = [];

    /// <summary>#218 / LRN-17: the overall floor placard, tethered to the weakest measured skill.</summary>
    public PlacementFloorViewModel Floor { get; init; } = new();

    /// <summary>
    /// #218 / LRN-20: the published 70% rule, in the learner's words. It is a sentence rather than a
    /// tooltip because a rule the learner has to hunt for is a rule that reads as an assertion.
    /// </summary>
    public string PassRule =>
        "A band is awarded when you answer at least 70% correctly at that band and at every lower band. "
        + "70% is well above the 25% a four-option guess reaches, so one lucky answer cannot carry a band. "
        + "If a lower band fails, your level stops at the highest band you passed all the way up.";

    /// <summary>
    /// Where a row's marker sits on the shared axis, as a percentage of the whole track: the bands before
    /// it, plus the fraction of its own band its position covers. Computed here rather than in the view so
    /// the geometry is one testable expression.
    /// </summary>
    public double TrackOffset(PlacementSkillRowViewModel row)
    {
        if (row.BandIndex is not int index || index < 0 || index >= Bands.Count) return 0;

        var total = Bands.Sum(b => b.Width);
        if (total <= 0) return 0;

        var before = Bands.Take(index).Sum(b => b.Width);
        var within = row.PositionInBand is { } position ? position / 100d * Bands[index].Width : 0;
        return Math.Clamp((before + within) / total * 100, 0, 100);
    }

    /// <summary>The one axis's column template, sized by the range each band covers.</summary>
    public string ColumnTemplate => Bands.Count == 0
        ? "none"
        : string.Join(" ", Bands.Select(b => $"{b.Width}fr"));

    /// <summary>
    /// How many body columns a spanned row covers. At least one, because a certificate that published no
    /// bands still needs a cell for its state panel — and the view may not call <c>Math.Max</c> itself
    /// (AttemptPresentationArchitecture forbids LINQ or arithmetic aggregates in a Razor view).
    /// </summary>
    public int BandSpan => Math.Max(1, Bands.Count);

    /// <summary>
    /// Each band's share of the axis, as a 0-1 fraction. The view multiplies it by the track's own width
    /// (<c>calc((100% - 17rem) * f)</c>), because a plain percentage in a <c>col</c> resolves against the
    /// whole table — including the skill-label column — and the columns then overrun it.
    /// </summary>
    public IReadOnlyList<double> ColumnFractions
    {
        get
        {
            var total = Bands.Sum(b => b.Width);
            return total <= 0
                ? []
                : Bands.Select(b => Math.Round(b.Width / (double)total, 6)).ToList();
        }
    }

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

    /// <summary>
    /// The status as the page compares it, in the schema's snake_case.
    /// <para>
    /// <b>Both spellings are accepted on purpose.</b> <c>PlacementEndpoints.DtoMapping</c> currently sends
    /// <c>Status.ToString()</c> — "NotMeasured", "EstimatedFromOneTask" — while this page, its tests and
    /// the database all speak <c>not_measured</c>. Comparing only the lower-case form made every real
    /// response read as "not measured", including the ones that were measured. Normalising here is the
    /// read-side seam that cannot be bypassed, and the mismatch itself is reported rather than papered
    /// over: the wire format is the API's to fix.
    /// </para>
    /// </summary>
    public static string NormalizeStatus(string? status)
    {
        if (string.IsNullOrEmpty(status)) return string.Empty;
        if (status.Contains('_', StringComparison.Ordinal)) return status.ToLowerInvariant();

        var builder = new System.Text.StringBuilder(status.Length + 4);
        for (var i = 0; i < status.Length; i++)
        {
            if (char.IsUpper(status[i]) && i > 0) builder.Append('_');
            builder.Append(char.ToLowerInvariant(status[i]));
        }

        return builder.ToString();
    }

    /// <summary>A skill still in the AI queue - a wait, not a failure.</summary>
    public static bool IsGrading(PlacementSkillResultInfoDto skill) => NormalizeStatus(skill.Status) == "grading";

    /// <summary>#217 / PLC-35: the retry is offered exactly when some task's grading failed.</summary>
    public bool CanRetryGrading =>
        SkillResults.Any(skill => NormalizeStatus(skill.Status) == "not_measured"
            && skill.FailureReason == "ai_grading_failed");

    public string RetryUrl { get; init; } = string.Empty;

    /// <summary>Words a row prints when it carries no level. Never a code, never a low band.</summary>
    public const string NoLevel = "Not measured";

    /// <summary>`B2 (78/100)` — a level and a position, never a band and never a bare percentage.</summary>
    public string LevelAndPosition(PlacementSkillResultInfoDto skill) =>
        skill is { LevelCode: { } level, PositionInBand: { } position }
            ? $"{level} ({position}/100)"
            : NoLevel;

    /// <summary>
    /// #217 / PLC-19: what the per-skill table prints in the Level column. A `not_measured` or `grading`
    /// row says so in words and never prints a level - a zero or "A1" here is the fabricated bottom band
    /// the ticket forbids (PLC-35's "neither zero nor A1").
    /// </summary>
    public string LevelCell(PlacementSkillResultInfoDto skill) => NormalizeStatus(skill.Status) switch
    {
        "grading" => "Grading",
        "not_measured" => NoLevel,
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

    /// <summary>
    /// The date the row behind a raw score was measured, so the evidence table can say how old each
    /// number is. Empty when no per-skill row exists for it. This lives here rather than in the view
    /// because a Razor view renders prepared data and never queries it (AttemptPresentationArchitecture).
    /// </summary>
    public string MeasuredOnFor(int skillId) =>
        SkillResults.FirstOrDefault(r => r.SkillId == skillId) is { MeasuredAt: { } at }
            ? Dates.FormatCustom(at, "d MMMM yyyy")
            : "not recorded";
}
