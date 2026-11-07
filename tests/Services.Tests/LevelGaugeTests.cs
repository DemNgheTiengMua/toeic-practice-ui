using System.Linq;
using BusinessObjects;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #203 / ADM-08: the level control is a set of live gauges that says which band the measured text
/// actually fits.
/// <para>
/// Every figure here is <see cref="GateTwo"/>'s: the bound is <see cref="GateTwo.Profile"/>'s and the
/// measurement is <see cref="GateTwo.Measure"/>'s. So a gauge that disagrees with the gate is a
/// disagreement with itself, and the fixture is the B1 passage <c>GateTwoTests</c> already holds inside
/// every bound - a passage nobody calibrated could not tell a passing gauge from a broken one.
/// </para>
/// </summary>
public sealed class LevelGaugeTests
{
    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> a control that only reports the selected
    /// band. A B1 passage read against C1 shows four failing gauges and must <em>name</em> B1, which is
    /// the whole point of ADM-08 - the author selects the band they intend, and the control shows the
    /// band the text demands.
    /// </summary>
    [Fact]
    public void A_B1_passage_read_against_C1_names_B1_as_the_band_it_fits()
    {
        CefrLevel.TryParseAuthored("C1", out var selected);

        var reading = LevelGaugeReader.Read("reading", selected, PoolGateTwoTests.B1Passage);

        Assert.False(reading.FitsSelected);
        Assert.Equal(CefrLevel.B1, reading.Fits);
        Assert.Contains("B1", reading.Verdict, System.StringComparison.Ordinal);
        Assert.Contains("C1", reading.Verdict, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The band the text fits and the band selected agree: four gauges, every one inside its bound, and
    /// the verdict says so in one sentence rather than leaving the author to compare four numbers.
    /// </summary>
    [Fact]
    public void A_B1_passage_read_against_B1_is_inside_every_gauge()
    {
        CefrLevel.TryParseAuthored("B1", out var selected);

        var reading = LevelGaugeReader.Read("reading", selected, PoolGateTwoTests.B1Passage);

        Assert.True(reading.FitsSelected);
        Assert.Equal(CefrLevel.B1, reading.Fits);
        Assert.Equal(4, reading.Gauges.Count);
        Assert.All(reading.Gauges, gauge => Assert.True(gauge.Inside, $"{gauge.Label} = {gauge.Value} outside {gauge.Target}"));
    }

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> a gauge that reports the measurement but
    /// no bound, or a bound taken from the selected band rather than the one being compared. A short
    /// text read at B1 must show the word count <em>and</em> B1's 220-300, so the author can see how far
    /// off the text is rather than only that it is off.
    /// </summary>
    [Fact]
    public void A_short_passage_reports_its_word_count_against_the_bands_own_bound()
    {
        CefrLevel.TryParseAuthored("B1", out var selected);

        var reading = LevelGaugeReader.Read("reading", selected, "The depot opens at six in the morning.");

        var words = Assert.Single(reading.Gauges, gauge => gauge.Label == "Words");
        Assert.Equal("8", words.Value);
        Assert.Equal("220-300", words.Target);
        Assert.False(words.Inside);
    }

    /// <summary>
    /// A listening text is measured on its own D0.2 column, which is a shorter envelope than reading's
    /// at the identical band. <b>Which guard could this pass while being wrong:</b> profiling a
    /// transcript against the reading bounds refuses every legal listening script as too short.
    /// </summary>
    [Fact]
    public void A_listening_transcript_is_profiled_against_the_listening_column()
    {
        CefrLevel.TryParseAuthored("A2", out var selected);

        var transcript = string.Join(" ", Enumerable.Repeat(
            "The train leaves at nine and the bus leaves at ten.", 5));

        var reading = LevelGaugeReader.Read("listening", selected, transcript);

        var words = Assert.Single(reading.Gauges, gauge => gauge.Label == "Words");
        Assert.Equal("60-95", words.Target);
    }

    /// <summary>
    /// The acoustic rate is not shown for a listening text, and the reading says why. A wpm figure
    /// derived from a word count is arithmetic, not evidence: D0.2 bounds the recorded speech and
    /// <c>Stimuli</c> stores no duration.
    /// </summary>
    [Fact]
    public void A_listening_gauge_set_says_the_acoustic_rate_was_not_measured()
    {
        CefrLevel.TryParseAuthored("B1", out var selected);

        var reading = LevelGaugeReader.Read("listening", selected, "Some script words here.");

        Assert.Contains(reading.Notices, notice => notice.Contains("acoustic rate", System.StringComparison.Ordinal));
        Assert.DoesNotContain(reading.Gauges, gauge => gauge.Label.Contains("rate", System.StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A productive prompt is not profiled, and the gauge set stays empty rather than inventing four
    /// zeros. The rubric measures learner output; D0.2 states no bounds for the prompt itself.
    /// </summary>
    [Fact]
    public void A_productive_prompt_carries_no_gauges_and_says_why()
    {
        CefrLevel.TryParseAuthored("B2", out var selected);

        var reading = LevelGaugeReader.Read("writing", selected, "Write a report about the incident.");

        Assert.Empty(reading.Gauges);
        Assert.Null(reading.Fits);
        Assert.NotEmpty(reading.Notices);
    }

    /// <summary>
    /// A text fitting no band is reported as fitting none, not as fitting the nearest. Picking the
    /// closest would name a band whose own bounds refuse the text.
    /// </summary>
    [Fact]
    public void A_text_outside_all_six_bands_fits_none()
    {
        CefrLevel.TryParseAuthored("B1", out var selected);

        var reading = LevelGaugeReader.Read("reading", selected, "One. Two. Three.");

        Assert.False(reading.FitsSelected);
    }
}
