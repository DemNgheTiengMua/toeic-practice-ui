using Contracts;
using Microsoft.Extensions.Logging;
using WebMVC.Formatters;
using DisplayDates = WebMVC.Formatters.DateTimeFormatter;

namespace WebMVC.Models;

/// <summary>
/// #218 / LRN-16, LRN-18, LRN-19: one skill as a row on the result signboard.
/// <para>
/// <b><see cref="Coordinate"/> is a level AND a position, never a percentage.</b> "B1 (42/100)" says
/// which band and where inside it. A bare "42%" reads as a score, and after a band crossing it reads as
/// a collapse — which is the misreading LRN-19 exists to prevent.
/// </para>
/// <para>
/// <b>A row with no measurement is a state, not a low band.</b> <see cref="IsStatePanel"/> rows carry no
/// marker at all, so they cannot sit at the bottom of the axis: the fabricated bottom band (#211, #217)
/// is not representable here.
/// </para>
/// </summary>
public sealed record PlacementSkillRowViewModel
{
    public required string SkillName { get; init; }

    /// <summary>measured, estimated, grading, not-measured or exempt. Internal; the page prints words.</summary>
    public required string State { get; init; }

    /// <summary>"B1 (42/100)" for a measurement; the state in words otherwise.</summary>
    public required string Coordinate { get; init; }

    /// <summary>
    /// The certificate's own name for the band this row sits in — "Intermediate" beside "B1 (42/100)".
    /// The level and the band are different quantities (PRN-03), so the row prints both rather than
    /// leaving the reader to assume the code and the column are the same thing.
    /// </summary>
    public string? BandName { get; init; }

    /// <summary>Which of the certificate's own bands this level falls in, or null when none names it.</summary>
    public int? BandIndex { get; init; }

    public int? PositionInBand { get; init; }

    /// <summary>The sentence under the coordinate for a state panel. Null for a measurement.</summary>
    public string? Note { get; init; }

    /// <summary>"Measured on 8 November 2026" — a skill measured in this sitting.</summary>
    public string? MeasuredOnText { get; init; }

    /// <summary>"Carried over from your sitting on 18 October 2026" — the badge's own sentence.</summary>
    public string? CarriedOverText { get; init; }

    /// <summary>
    /// True when this number predates this sitting. Driven by the measurement date, not only by the
    /// reuse flag: the date is the fact, the flag is one way of setting it.
    /// </summary>
    public bool IsCarriedOver { get; init; }

    /// <summary>One rubric task graded and the other not (PLC-35): the figure is AI-derived and partial.</summary>
    public bool IsAiEstimated { get; init; }

    /// <summary>"14 of 20 correct" — the evidence behind the coordinate, when the sitting gave any.</summary>
    public string AttemptText { get; init; } = string.Empty;

    /// <summary>
    /// Set when the level is real but this certificate publishes no band of that name — the honest state
    /// for a certificate whose bands are CAT_B1 / SW_B1 (PRN-03). Nothing is drawn on the axis.
    /// </summary>
    public string? NoBandNote { get; init; }

    public string? FailureReason { get; init; }

    /// <summary>True only for a failed grading. A queued job is a wait, not a failure.</summary>
    public bool CanRetry { get; init; }

    /// <summary>The track's meaning in words, for a reader who cannot see the marker.</summary>
    public string TrackAriaLabel { get; init; } = string.Empty;

    public bool IsMeasured => State is "measured" or "estimated";

    /// <summary>The states that occupy no band column.</summary>
    public bool IsStatePanel => State is "not-measured" or "grading" or "exempt";

    /// <summary>Draws a marker only where there is both a level and a column of the certificate to put it in.</summary>
    public bool HasMarker => IsMeasured && BandIndex is not null;
}

/// <summary>
/// #218: builds the horizon rows from what the result response already carries.
/// <para>
/// <b>The axis is the certificate's own bands, never the CEFR ladder.</b> A row's column is found by
/// matching its level code against the certificate's band codes. Where the certificate names no band
/// for that level the row carries <see cref="PlacementSkillRowViewModel.NoBandNote"/> and no marker —
/// inventing a mapping from "D1" to "A1" is the crosswalk PRN-03 refuses.
/// </para>
/// </summary>
public static class PlacementHorizon
{
    private const string Measured = "measured";
    private const string Estimated = "estimated";
    private const string Grading = "grading";
    private const string NotMeasured = "not-measured";
    private const string Exempt = "exempt";

    public static IReadOnlyList<PlacementSkillRowViewModel> Build(
        PlacementResultViewDto result, IReadOnlyList<PlacementBandColumnViewModel> bands,
        DisplayDates dates, ILogger logger)
    {
        var scores = result.SkillScores.ToDictionary(s => s.SkillId);
        var taken = result.TakenAt.UtcDateTime.Date;
        var rows = new List<PlacementSkillRowViewModel>();

        foreach (var skill in result.SkillResults ?? [])
        {
            var state = StateOf(skill.Status);
            var name = string.IsNullOrWhiteSpace(skill.SkillName)
                // #220 owns this fallback; the registry's own word is reused rather than a new one.
                ? ModalityLabels.LabelForCode(null, logger)
                : skill.SkillName;
            var score = scores.GetValueOrDefault(skill.SkillId);
            var scored = state is Measured or Estimated;
            var level = scored ? CefrLevelLabels.LabelForCode(skill.LevelCode, logger) : null;
            var bandIndex = scored ? BandIndexFor(skill.LevelCode, bands) : null;
            // A measurement dated before this sitting is what makes a figure carried over. The flag is
            // one way that happens; the date is the fact, so an older date badges on its own.
            var carried = skill.ReusedFromPrior
                || (scored && skill.MeasuredAt is { } at && at.UtcDateTime.Date < taken);

            rows.Add(new PlacementSkillRowViewModel
            {
                SkillName = name,
                State = state,
                Coordinate = scored ? Coordinate(level!, skill.PositionInBand) : StateWords(state),
                BandIndex = bandIndex,
                BandName = bandIndex is int column ? bands[column].Label : null,
                PositionInBand = scored ? skill.PositionInBand : null,
                Note = scored ? null : NoteFor(state),
                MeasuredOnText = scored && !carried && skill.MeasuredAt is { } measured
                    ? $"Measured on {dates.FormatCustom(measured, "d MMMM yyyy")}"
                    : null,
                CarriedOverText = carried && skill.MeasuredAt is { } earlier
                    ? $"Carried over from your sitting on {dates.FormatCustom(earlier, "d MMMM yyyy")}"
                    : null,
                IsCarriedOver = carried,
                IsAiEstimated = state == Estimated,
                AttemptText = score is null ? string.Empty : $"{score.RawScore} of {score.TotalQuestions} correct",
                NoBandNote = scored && bandIndex is null ? NoBandNote(level!) : null,
                FailureReason = skill.FailureReason,
                CanRetry = state == NotMeasured && skill.FailureReason == "ai_grading_failed",
                TrackAriaLabel = scored
                    ? $"{name}: {level}, position {skill.PositionInBand?.ToString() ?? "unknown"} of 100 in the band"
                    : $"{name}: {StateWords(state)}",
            });
        }

        return rows;
    }

    private static string Coordinate(string level, int? position) =>
        position is { } at ? $"{level} ({at}/100)" : level;

    private static string StateWords(string state) => state switch
    {
        Grading => "Grading",
        Exempt => "Exempt",
        _ => "Not measured",
    };

    private static string? NoteFor(string state) => state switch
    {
        Grading => "AI grading is running on this skill. This row fills in on its own; nothing is missing yet.",
        Exempt => "This skill was accommodated out of your result, so it is not part of your overall.",
        _ => null,
    };

    private static string NoBandNote(string level) =>
        $"This certificate's own bands name no column for {level}. Your level is recorded; it is not drawn "
        + "on the horizon, because that would mean converting one scale into another.";

    private static string StateOf(string status) => PlacementResultViewModel.NormalizeStatus(status) switch
    {
        "measured" => Measured,
        "estimated_from_one_task" => Estimated,
        "grading" => Grading,
        "exempt_accommodated" => Exempt,
        _ => NotMeasured,
    };

    /// <summary>
    /// The certificate's own band for a level, by name. Only an exact code match counts: a certificate
    /// that names its bands CAT_B1 / D1 has no column for "B1", and pretending it does is the crosswalk
    /// PRN-03 forbids.
    /// </summary>
    private static int? BandIndexFor(string? levelCode, IReadOnlyList<PlacementBandColumnViewModel> bands)
    {
        if (string.IsNullOrEmpty(levelCode)) return null;

        for (var i = 0; i < bands.Count; i++)
        {
            if (string.Equals(bands[i].Code, levelCode, StringComparison.OrdinalIgnoreCase)) return i;
        }

        // "Below A1" is the one band a certificate may declare for the level under A1, and pre_a1 is
        // that same statement in the level vocabulary — one band under two spellings, not two scales.
        if (string.Equals(levelCode, "pre_a1", StringComparison.OrdinalIgnoreCase))
        {
            for (var i = 0; i < bands.Count; i++)
            {
                if (string.Equals(bands[i].Code, "below_A1", StringComparison.OrdinalIgnoreCase)) return i;
            }
        }

        return null;
    }
}
