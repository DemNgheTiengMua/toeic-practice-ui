using System.Collections.Generic;
using System.Linq;
using BusinessObjects;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #203 / D0.3 and D0.4: the studio's two shape rules, and the gauges beside them.
/// <para>
/// These are the rules no gate states. Gate one judges an item's options; gate two judges one text
/// against one band; gate four judges a bundle's spans. None of them compares a child's level to its
/// stimulus's, and none bounds how many children a stimulus carries - which is why a C1 inference item
/// on a 130-word A2 notice passes every gate and is still the defect the specification names.
/// </para>
/// </summary>
public sealed class StimulusEnvelopeTests
{
    // -----------------------------------------------------------------------------------------
    // AC1: the child count is locked by the stimulus's skill and level
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> a quota keyed on the level alone. CNT-21
    /// packs A2 as two items per audio and three per passage, so a level-only table gives one of the
    /// two the wrong count - and the screen would pre-create one slot too many or one too few for
    /// every A2 bundle of that skill.
    /// </summary>
    [Theory]
    // CNT-21's listening row: 2, 2, 3, 3, 4, 3.
    [InlineData("listening", "A1", 2)]
    [InlineData("listening", "A2", 2)]
    [InlineData("listening", "B1", 3)]
    [InlineData("listening", "B2", 3)]
    [InlineData("listening", "C1", 4)]
    [InlineData("listening", "C2", 3)]
    // CNT-21's reading row: 2-3, 3, 3, 3-4, 3-4, 3-4.
    [InlineData("reading", "A1", 2)]
    [InlineData("reading", "A2", 3)]
    [InlineData("reading", "B1", 3)]
    [InlineData("reading", "B2", 4)]
    [InlineData("reading", "C1", 4)]
    [InlineData("reading", "C2", 4)]
    public void The_locked_child_count_is_the_volume_tables_modal_pack_for_that_skill_and_band(
        string modality, string level, int expected)
    {
        CefrLevel.TryParseAuthored(level, out var band);

        var quota = StimulusEnvelope.Quote(modality, band, "passage_comprehension");

        Assert.Equal(expected, quota.Locked);
    }

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> locking the count exactly. CNT-21 states
    /// exceptions - at A1 one passage in thirty-seven carries three, at B2/C1/C2 a few carry three
    /// instead of four - so a rule demanding the modal count would make those rows un-authorable. The
    /// save bound is D0.4's 2-4; the lock is what the screen pre-creates.
    /// </summary>
    [Fact]
    public void The_count_is_locked_for_the_slots_but_bounded_by_D04_for_the_save()
    {
        CefrLevel.TryParseAuthored("B2", out var b2);
        var quota = StimulusEnvelope.Quote("reading", b2, "passage_comprehension");

        Assert.Equal(4, quota.Locked);
        Assert.Equal(2, quota.Minimum);
        Assert.Equal(4, quota.Maximum);
        Assert.True(quota.Minimum < quota.Locked, "the exception rows could not be saved if the minimum were the lock");
    }

    /// <summary>
    /// D0.4's two standalone reading types carry exactly one item. <b>Which guard could this pass while
    /// being wrong:</b> treating a sentence_cloze as a shared stimulus accepts four children on one
    /// fifteen-word sentence, and each of them then reports a span overlap against the others.
    /// </summary>
    [Theory]
    [InlineData("sentence_cloze")]
    [InlineData("text_insertion_cohesion")]
    public void A_standalone_reading_type_carries_exactly_one_child(string itemType)
    {
        CefrLevel.TryParseAuthored("B1", out var b1);

        var quota = StimulusEnvelope.Quote("reading", b1, itemType);

        Assert.Equal(1, quota.Locked);
        Assert.Equal(1, quota.Minimum);
        Assert.Equal(1, quota.Maximum);
    }

    /// <summary>
    /// The studio authors the two objective skills. A speaking prompt is one task with a rubric, and
    /// pre-creating four child slots for it would be a shape the schema cannot hold.
    /// </summary>
    [Theory]
    [InlineData("speaking")]
    [InlineData("writing")]
    public void A_productive_skill_is_not_a_studio_skill(string modality)
    {
        CefrLevel.TryParseAuthored("B1", out var b1);

        Assert.False(StimulusEnvelope.IsStudioSkill(modality));
        Assert.Throws<System.ArgumentException>(() => StimulusEnvelope.Quote(modality, b1, "descriptive_monologue"));
    }

    // -----------------------------------------------------------------------------------------
    // AC3: D0.3's ±1 envelope
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input this guard must refuse.</b> A C1 inference child on an A2 stimulus. Every per-item
    /// gate accepts it: gate one sees four good options, gate two profiles the notice against A2 and
    /// finds it in band, gate four classifies the <em>child</em> at C1 and agrees. Nothing compares the
    /// two levels unless this rule does.
    /// </summary>
    [Fact]
    public void At_A2_every_child_must_sit_at_A2_exactly()
    {
        CefrLevel.TryParseAuthored("A2", out var a2);

        var refusals = StimulusEnvelope.Review(
            "reading", a2, "passage_comprehension", [CefrLevel.A2, CefrLevel.B1], "char");

        Assert.Contains(refusals, refusal => refusal.Rule == StimulusStudioRules.ChildLevel);
    }

    /// <summary>
    /// The other half: at B1 and above one level either side is inside the envelope, so a B1 passage
    /// may carry an A2 or a B2 child. A rule that refused any difference would refuse the normal shape.
    /// </summary>
    [Fact]
    public void At_B1_a_child_may_sit_one_level_either_side()
    {
        CefrLevel.TryParseAuthored("B1", out var b1);

        var refusals = StimulusEnvelope.Review(
            "reading", b1, "passage_comprehension",
            [CefrLevel.B1, CefrLevel.B1, CefrLevel.A2], "char");

        Assert.DoesNotContain(refusals, refusal => refusal.Rule == StimulusStudioRules.ChildLevel);
    }

    /// <summary>
    /// Two levels away is outside the envelope at every band. The pair is deliberate: A2 and B2 both
    /// exist and the only thing wrong with them is the distance.
    /// </summary>
    [Fact]
    public void At_B2_a_child_two_levels_away_is_refused()
    {
        CefrLevel.TryParseAuthored("B2", out var b2);

        var refusals = StimulusEnvelope.Review(
            "reading", b2, "passage_comprehension", [CefrLevel.B2, CefrLevel.C2], "char");

        Assert.Contains(refusals, refusal => refusal.Rule == StimulusStudioRules.ChildLevel);
    }

    // -----------------------------------------------------------------------------------------
    // AC3: the two-thirds rule
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input this guard must refuse.</b> Four children on a B2 passage, one at B2 and three at
    /// B1: every child is inside the ±1 envelope, so the level rule passes, and only two-thirds catches
    /// that this is a B1 bundle wearing a B2 stimulus.
    /// </summary>
    [Fact]
    public void Four_children_with_one_at_the_stimulus_level_are_refused_by_the_two_thirds_rule()
    {
        CefrLevel.TryParseAuthored("B2", out var b2);

        var refusals = StimulusEnvelope.Review(
            "reading", b2, "passage_comprehension",
            [CefrLevel.B2, CefrLevel.B1, CefrLevel.B1, CefrLevel.B1], "char");

        var refusal = Assert.Single(refusals, r => r.Rule == StimulusStudioRules.AtLevelShare);
        Assert.Contains("1 of 4", refusal.Message, System.StringComparison.Ordinal);
        Assert.Contains("3", refusal.Message, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Three of four satisfies the rule: 75% is above two-thirds, and refusing it would refuse the
    /// shape the content volume table calls normal.
    /// </summary>
    [Fact]
    public void Three_of_four_at_the_stimulus_level_is_accepted()
    {
        CefrLevel.TryParseAuthored("B2", out var b2);

        var refusals = StimulusEnvelope.Review(
            "reading", b2, "passage_comprehension",
            [CefrLevel.B2, CefrLevel.B2, CefrLevel.B2, CefrLevel.B1], "char");

        Assert.DoesNotContain(refusals, r => r.Rule == StimulusStudioRules.AtLevelShare);
    }

    /// <summary>
    /// Two children need both, because one of two is 50% and two-thirds of two rounds up. A guard that
    /// truncated instead of rounding up accepts it.
    /// </summary>
    [Fact]
    public void Two_children_need_both_at_the_stimulus_level()
    {
        CefrLevel.TryParseAuthored("C1", out var c1);

        var refusals = StimulusEnvelope.Review(
            "reading", c1, "passage_comprehension", [CefrLevel.C1, CefrLevel.B2], "char");

        Assert.Contains(refusals, r => r.Rule == StimulusStudioRules.AtLevelShare);
    }

    // -----------------------------------------------------------------------------------------
    // AC3: the span unit, and the count bound at save
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> accepting any unit string. A listening
    /// child whose span says "char" is compared as seconds by gate four's gap rule, which then reports
    /// a gap of hundreds of seconds for a bundle nobody timed (CNT-11).
    /// </summary>
    [Fact]
    public void A_listening_childs_span_must_be_in_seconds()
    {
        CefrLevel.TryParseAuthored("B1", out var b1);

        var refusals = StimulusEnvelope.Review(
            "listening", b1, "monologue_talk",
            [CefrLevel.B1, CefrLevel.B1, CefrLevel.B1], "char");

        Assert.Contains(refusals, refusal => refusal.Rule == StimulusStudioRules.SpanUnit);
    }

    /// <summary>
    /// A shared stimulus needs at least two children, and no more than four. One child is a standalone
    /// item wearing a passage, and five is a testlet CNT-21 never packs.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void A_shared_bundle_outside_two_to_four_children_is_refused(int count)
    {
        CefrLevel.TryParseAuthored("B1", out var b1);
        var children = Enumerable.Repeat(CefrLevel.B1, count).ToList();

        var refusals = StimulusEnvelope.Review(
            "reading", b1, "passage_comprehension", children, "char");

        Assert.Contains(refusals, refusal => refusal.Rule == StimulusStudioRules.ChildCount);
    }

    /// <summary>A complete, legal bundle is refused by nothing: the guard must be able to pass.</summary>
    [Fact]
    public void A_legal_bundle_is_refused_by_no_shape_rule()
    {
        CefrLevel.TryParseAuthored("B1", out var b1);

        var refusals = StimulusEnvelope.Review(
            "reading", b1, "passage_comprehension",
            [CefrLevel.B1, CefrLevel.B1, CefrLevel.A2], "char");

        Assert.Empty(refusals);
    }
}
