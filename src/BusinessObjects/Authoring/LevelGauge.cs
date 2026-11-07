using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BusinessObjects;

/// <summary>#203: one live gauge - what the text measures, and the bound the selected band sets.</summary>
public sealed record LevelGauge(string Label, string Value, string Target, bool? Inside);

/// <summary>
/// #203 / ADM-08: what the level control shows instead of a dropdown, and which band the text
/// actually fits.
/// <para>
/// <b>Every number here is <see cref="GateTwo"/>'s.</b> The bound is
/// <see cref="GateTwo.Profile"/>'s and the measurement is <see cref="GateTwo.Measure"/>'s, so the gauge
/// an author reads and the refusal gate two writes cannot disagree - which is the whole reason a level
/// control exists: an author selects the band they intend, and ADM-08 says the control must show the
/// band the text demands.
/// </para>
/// <para>
/// <b>Four gauges, and which fourth depends on the skill.</b> ADM-08 lists "word count or duration,
/// lexical coverage, MLTU and grade level, acoustic rate". The duration-based acoustic rate needs the
/// recording's length, and <c>Stimuli</c> stores no duration - <see cref="GateTwo"/> names the same gap
/// - so a listening text shows clauses per T-unit as its fourth gauge and the result records that the
/// acoustic rate was not measured. Inventing a duration from a word count would produce a wpm figure
/// that is arithmetic rather than evidence.
/// </para>
/// </summary>
public sealed record LevelGaugeReading(
    CefrLevel Selected,
    CefrLevel? Fits,
    bool FitsSelected,
    IReadOnlyList<LevelGauge> Gauges,
    IReadOnlyList<string> Notices)
{
    /// <summary>
    /// The sentence the control prints. Stated as a fact with the two bands named, because "the text
    /// does not match" leaves the author to guess which way (UI copy rule: no praise, no promises).
    /// </summary>
    public string Verdict =>
        Fits is null
            ? $"The text fits none of the six bands; {Selected.Code} is selected."
            : FitsSelected
                ? $"The text fits {Selected.Code}, the band selected."
                : $"The text fits {Fits.Value.Code}; {Selected.Code} is selected.";
}

public static class LevelGaugeReader
{
    /// <summary>
    /// Measures <paramref name="text"/> against the band the author selected, and reports the band it
    /// actually fits. <paramref name="modality"/> is D0.2's column: listening and reading are profiled,
    /// productive prompts are not (their bounds are learner-output demands).
    /// </summary>
    public static LevelGaugeReading Read(string? modality, CefrLevel selected, string? text)
    {
        var column = string.Equals(modality, "listening", StringComparison.Ordinal)
            ? GateTwoModality.Listening
            : string.Equals(modality, "reading", StringComparison.Ordinal)
                ? GateTwoModality.Reading
                : GateTwoModality.Productive;

        if (column == GateTwoModality.Productive)
        {
            return new LevelGaugeReading(
                selected,
                null,
                false,
                [],
                ["A speaking or writing prompt is not profiled by these gauges: D0.2's bounds for those skills describe learner output, which the rubric measures."]);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return new LevelGaugeReading(
                selected,
                null,
                false,
                [],
                [$"There is no {(column == GateTwoModality.Listening ? "transcript" : "passage")} to measure yet."]);
        }

        var fits = Fit(modality, text);
        var measured = GateTwo.Measure(text, selected);
        var profile = GateTwo.Profile(selected, column)!;

        var gauges = new List<LevelGauge>
        {
            new(
                "Words",
                Number(measured.Words),
                $"{profile.MinimumWords}-{profile.MaximumWords}",
                measured.Words >= profile.MinimumWords && measured.Words <= profile.MaximumWords),
            new(
                "Lexical coverage",
                Percent(measured.LexicalCoverage),
                profile.HasLexicalBound ? $"at least {Number(profile.MinimumLexicalCoverage!.Value)}%" : "unrestricted",
                !profile.HasLexicalBound || measured.LexicalCoverage * 100 >= profile.MinimumLexicalCoverage),
            new(
                "Mean T-unit length",
                Number(measured.MeanTUnitLength),
                profile.MeanTUnitLength.Describe(),
                profile.MeanTUnitLength.Contains(measured.MeanTUnitLength)),
        };

        if (column == GateTwoModality.Reading && profile.FleschKincaid is { } readability)
        {
            gauges.Add(new LevelGauge(
                "Flesch-Kincaid grade",
                Number(measured.FleschKincaid),
                readability.Describe(),
                readability.Contains(measured.FleschKincaid)));
        }
        else if (profile.ClausesPerTUnit is { } clauses)
        {
            gauges.Add(new LevelGauge(
                "Clauses per T-unit",
                Number(measured.ClausesPerTUnit),
                clauses.Describe(),
                clauses.Contains(measured.ClausesPerTUnit)));
        }
        else
        {
            gauges.Add(new LevelGauge(
                "Sentences",
                Number(measured.Sentences),
                "measured, not bounded",
                null));
        }

        var notices = new List<string>();
        if (column == GateTwoModality.Listening)
        {
            notices.Add(
                "The acoustic rate is not measured: D0.2 bounds words per minute and Stimuli stores no recording length, so a wpm figure here would be arithmetic rather than evidence.");
        }

        return new LevelGaugeReading(selected, fits, fits == selected, gauges, notices);
    }

    /// <summary>
    /// The band whose whole profile the text satisfies, in ladder order, or <c>null</c> when it sits
    /// outside all six. The first match is reported rather than the closest: an overlapping pair of
    /// bands is a fact about the tables, and picking "nearest" would show the author a band that does
    /// not actually admit their text.
    /// </summary>
    public static CefrLevel? Fit(string? modality, string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !StimulusEnvelope.IsStudioSkill(modality))
        {
            return null;
        }

        foreach (var band in CefrLevel.AuthoredLevels)
        {
            if (Fits(modality!, band, text))
            {
                return band;
            }
        }

        return null;
    }

    /// <summary>Whether one band's every bound admits the text. Measured at that band, so coverage is its own.</summary>
    public static bool Fits(string modality, CefrLevel band, string text)
    {
        var column = string.Equals(modality, "listening", StringComparison.Ordinal)
            ? GateTwoModality.Listening
            : GateTwoModality.Reading;

        var verdict = GateTwo.Review(new GateTwoItem(text, band, column));
        return verdict.Accepted;
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(double value) =>
        Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    private static string Percent(double share) =>
        Math.Round(share * 100, 1).ToString("0.#", CultureInfo.InvariantCulture) + "%";
}
