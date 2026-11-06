using System;
using System.Collections.Generic;
using System.Linq;
using BusinessObjects;

namespace Services.Tests;

/// <summary>
/// #197: gate four's three rule groups that need no model - level consistency against a
/// classification already made, sibling span independence, and the audio bundle's order and gap.
/// <para>
/// Every test states, in a sentence, the input that would satisfy the guard while being wrong. The
/// three that matter most:
/// <list type="bullet">
/// <item><b>Overlap.</b> Dividing the intersection by the <em>stimulus</em> length instead of the
/// union passes two identical four-character spans on a thousand-character passage at 0.4%, which is
/// the exact defect CNT-12 exists to catch: the two items measure one sentence twice.</item>
/// <item><b>The gist exception.</b> Exempting on topic code alone passes an <c>R-GIST</c> item whose
/// evidence is a ten-word fragment, which is not a whole-passage gist question and does overlap its
/// sibling.</item>
/// <item><b>The gap.</b> Measuring start-to-start instead of end-to-start passes two items whose
/// spans are 30 seconds long and 5 seconds apart, so the learner hears the cue for item two while
/// still answering item one.</item>
/// </list>
/// </para>
/// </summary>
public sealed class GateFourTests
{
    // -----------------------------------------------------------------------------------------
    // level consistency: an independent classifier must predict the authored band
    // -----------------------------------------------------------------------------------------

    private static GateFourClassification Predicted(CefrLevel level, string family = "gemini") =>
        new GateFourClassification.Predicted(family, family + "-model", level);

    /// <summary>
    /// The accept direction. Without it a gate that refused every item would look like a guard: every
    /// refusal test below would still pass.
    /// </summary>
    [Fact]
    public void A_classifier_that_predicts_the_authored_band_is_accepted()
    {
        var refusals = GateFour.ReviewLevel("claude", CefrLevel.B1, Predicted(CefrLevel.B1));

        Assert.Empty(refusals);
    }

    /// <summary>
    /// D17: a disagreement refuses the item <em>and names the predicted band</em>. A refusal that only
    /// says "level mismatch" leaves the author re-reading every band in the table.
    /// </summary>
    [Fact]
    public void A_disagreeing_classifier_refuses_and_names_the_predicted_band()
    {
        var refusals = GateFour.ReviewLevel("claude", CefrLevel.B1, Predicted(CefrLevel.C1));

        var refusal = Assert.Single(refusals);
        Assert.Equal(GateFourRules.LevelDisagreement, refusal.Rule);
        Assert.Contains("C1", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("B1", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A classifier that did not run cannot satisfy a rule about what a classifier predicts. Modelling
    /// "unavailable" as a pass is how an environment with no classifier reports every item as
    /// level-consistent.
    /// </summary>
    [Fact]
    public void An_unavailable_classifier_refuses_rather_than_passing()
    {
        var refusals = GateFour.ReviewLevel(
            "claude", CefrLevel.B1, new GateFourClassification.Unavailable("no provider is configured"));

        var refusal = Assert.Single(refusals);
        Assert.Equal(GateFourRules.ClassifierUnavailable, refusal.Rule);
        Assert.Contains("no provider is configured", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// "An independent classifier" is a property of the declaration, and D17's three-family rule is
    /// the vocabulary it is stated in. <b>Which guard could this pass while being wrong:</b> a check
    /// on the predicted level alone accepts the authoring family re-reading its own item, which shares
    /// its own blind spots. The level here agrees, so only the independence rule can refuse it.
    /// </summary>
    [Fact]
    public void The_authoring_family_classifying_its_own_item_is_refused_as_not_independent()
    {
        var refusals = GateFour.ReviewLevel("claude", CefrLevel.B1, Predicted(CefrLevel.B1, "claude"));

        var refusal = Assert.Single(refusals);
        Assert.Equal(GateFourRules.ClassifierNotIndependent, refusal.Rule);
    }

    /// <summary>A classifier that declares no family cannot be shown to be independent of the author.</summary>
    [Fact]
    public void A_classifier_with_no_declared_family_is_refused_rather_than_assumed_distinct()
    {
        var refusals = GateFour.ReviewLevel(
            "claude", CefrLevel.B1, new GateFourClassification.Predicted("  ", "unnamed", CefrLevel.B1));

        Assert.Contains(refusals, r => r.Rule == GateFourRules.ClassifierNotIndependent);
    }

    // -----------------------------------------------------------------------------------------
    // sibling evidence spans: IoU < 0.15, with the whole-passage gist exception
    // -----------------------------------------------------------------------------------------

    private static GateFourSpan Span(string topic, int start, int end, int? id = null, string unit = "char") =>
        new(id, topic, start, end, unit);

    /// <summary>
    /// The trap the ticket names. Two spans over the same four characters on a hard reading passage
    /// have an IoU of 1.00 however long the passage is, because union is measured over the two spans
    /// and not over the passage. <b>Which guard could this pass while being wrong:</b>
    /// <c>intersection / stimulusLength</c> reports 0.4% here and accepts it.
    /// </summary>
    [Fact]
    public void Two_identical_spans_on_a_long_passage_are_refused()
    {
        var refusals = GateFour.ReviewSpans(
            [Span("R-DET", 10, 14, 1), Span("R-INF", 10, 14, 2)], stimulusLength: 1000);

        var refusal = Assert.Single(refusals);
        Assert.Equal(GateFourRules.SpanOverlap, refusal.Rule);
        Assert.Contains("1.00", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The ceiling is strict: 0.15 fails, 0.13 passes. An inclusive comparison admits the value the
    /// specification refuses, and the two cases sit two characters apart so nothing but the boundary
    /// rule can separate them.
    /// </summary>
    [Fact]
    public void Overlap_of_exactly_fifteen_percent_is_refused_and_thirteen_percent_is_accepted()
    {
        var atTheCeiling = GateFour.ReviewSpans(
            [Span("R-DET", 0, 100, 1), Span("R-INF", 85, 100, 2)], stimulusLength: 100);
        var below = GateFour.ReviewSpans(
            [Span("R-DET", 0, 100, 1), Span("R-INF", 87, 100, 2)], stimulusLength: 100);

        Assert.Single(atTheCeiling);
        Assert.Empty(below);
    }

    /// <summary>
    /// The exception itself, which the ticket calls the trap. A whole-passage gist item's span is the
    /// passage by definition, so it overlaps every sibling; without the exception no gist bundle is
    /// legal. Applied literally the rule refuses all of them, and the sibling here is deliberate: its
    /// 0.20 overlap is over the ceiling on its own.
    /// </summary>
    [Fact]
    public void A_whole_passage_gist_item_is_exempt_from_the_overlap_rule()
    {
        var refusals = GateFour.ReviewSpans(
            [Span("R-GIST", 0, 100, 1), Span("R-DET", 10, 30, 2)], stimulusLength: 100);

        Assert.Empty(refusals);
    }

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> exempting on the topic code alone accepts
    /// this pair, and this pair is the defect - a ten-word "gist" question measuring the same sentence
    /// its sibling measures. The exception is for the <em>whole passage</em>, not for the topic.
    /// </summary>
    [Fact]
    public void A_gist_item_whose_span_is_a_fragment_is_not_exempt()
    {
        var refusals = GateFour.ReviewSpans(
            [Span("R-GIST", 10, 40, 1), Span("R-DET", 20, 50, 2)], stimulusLength: 100);

        Assert.Contains(refusals, r => r.Rule == GateFourRules.SpanOverlap);
    }

    /// <summary>
    /// The exemption needs the passage length to be settled, and a check that could not run cannot
    /// pass. Skipping the pair instead reports a bundle with a fragment gist item as independent.
    /// </summary>
    [Fact]
    public void A_gist_bundle_with_no_passage_length_refuses_rather_than_guessing()
    {
        var refusals = GateFour.ReviewSpans(
            [Span("R-GIST", 0, 100, 1), Span("R-DET", 10, 30, 2)], stimulusLength: null);

        Assert.Contains(refusals, r => r.Rule == GateFourRules.GistExemptionUnsettled);
    }

    /// <summary>
    /// Siblings are not only consecutive. <b>Which guard could this pass while being wrong:</b> a loop
    /// over the sorted list's neighbouring pairs never compares items 1 and 3, so a bundle whose first
    /// and third children measure the same characters passes - which is the inflation CNT-12 names.
    /// </summary>
    [Fact]
    public void Non_adjacent_siblings_that_overlap_are_still_refused()
    {
        var refusals = GateFour.ReviewSpans(
            [Span("R-DET", 0, 20, 1), Span("R-INF", 40, 60, 2), Span("R-ATT", 10, 30, 3)],
            stimulusLength: 200);

        var refusal = Assert.Single(refusals);
        Assert.Contains("item 1", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("item 3", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// One span in characters and one in seconds is not a pair whose overlap can be computed.
    /// <b>Which guard could this pass while being wrong:</b> comparing the integers anyway reports a
    /// meaningless fraction, and skipping the pair accepts a bundle nobody measured.
    /// </summary>
    [Fact]
    public void Spans_in_different_units_are_refused_rather_than_compared_as_bare_numbers()
    {
        var refusals = GateFour.ReviewSpans(
            [Span("R-DET", 0, 100, 1), Span("R-INF", 40, 90, 2, unit: "sec")], stimulusLength: 500);

        var refusal = Assert.Single(refusals);
        Assert.Equal(GateFourRules.SpanUnit, refusal.Rule);
    }

    /// <summary>A standalone item has no sibling to overlap; refusing it would make every cloze illegal.</summary>
    [Fact]
    public void A_standalone_item_has_no_sibling_to_overlap()
    {
        Assert.Empty(GateFour.ReviewSpans([Span("R-GRAM", 0, 40, 1)], stimulusLength: 40));
        Assert.Empty(GateFour.ReviewSpans([], stimulusLength: null));
    }

    // -----------------------------------------------------------------------------------------
    // audio bundles: chronological order, with L-GIST at sequence 1, and the 8.0 s gap
    // -----------------------------------------------------------------------------------------

    private static GateFourAudioChild Child(int sequence, string topic, int start, int end, string unit = "sec") =>
        new(sequence, topic, start, end, unit);

    /// <summary>The accept direction: chronological, eight seconds of air, no refusal.</summary>
    [Fact]
    public void A_chronological_bundle_with_an_eight_second_gap_is_accepted()
    {
        var refusals = GateFour.ReviewAudio([Child(1, "L-DET", 0, 10), Child(2, "L-DET", 18, 30)]);

        Assert.Empty(refusals);
    }

    /// <summary>
    /// The gap is end-to-start, not start-to-start. <b>Which guard could this pass while being wrong:</b>
    /// these items start 35 seconds apart, so a start-to-start comparison reports a gap of 35 and
    /// accepts; the learner has 5 seconds between the end of one span and the cue for the next.
    /// </summary>
    [Fact]
    public void The_gap_is_measured_from_the_end_of_the_earlier_span_not_its_start()
    {
        var refusals = GateFour.ReviewAudio([Child(1, "L-DET", 0, 30), Child(2, "L-DET", 35, 45)]);

        var refusal = Assert.Single(refusals);
        Assert.Equal(GateFourRules.AudioGap, refusal.Rule);
        Assert.Contains("5", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>Seven seconds is below the floor; eight is exactly at it.</summary>
    [Fact]
    public void Seven_seconds_apart_is_refused_and_eight_is_the_floor()
    {
        Assert.Single(GateFour.ReviewAudio([Child(1, "L-DET", 0, 10), Child(2, "L-DET", 17, 30)]));
        Assert.Empty(GateFour.ReviewAudio([Child(1, "L-DET", 0, 10), Child(2, "L-DET", 18, 30)]));
    }

    /// <summary>CNT-10 names detail <em>or inference</em> items, so an inference pair is in scope.</summary>
    [Fact]
    public void An_inference_pair_is_held_to_the_gap_as_a_detail_pair_is()
    {
        var refusals = GateFour.ReviewAudio([Child(1, "L-DET", 0, 10), Child(2, "L-INF", 17, 30)]);

        Assert.Single(refusals);
    }

    /// <summary>
    /// CNT-10 scopes the gap to detail and inference items, so two attitude items are ordered but not
    /// gapped. <b>Which guard could this pass while being wrong:</b> a universal gap refuses this legal
    /// bundle, and the over-refusal is invisible because a gate that refuses everything still looks
    /// strict.
    /// </summary>
    [Fact]
    public void Two_attitude_items_are_ordered_but_not_held_to_the_detail_gap()
    {
        var refusals = GateFour.ReviewAudio([Child(1, "L-ATT", 0, 5), Child(2, "L-ATT", 6, 9)]);

        Assert.Empty(refusals);
    }

    /// <summary>
    /// The exception CNT-10 grants: an L-GIST child at sequence 1 is the sole item allowed out of
    /// chronological order. Its span starts <em>after</em> its sibling's, so the ordering rule alone
    /// would refuse it; the exemption is what makes every gist bundle legal.
    /// </summary>
    [Fact]
    public void The_gist_item_at_sequence_one_is_exempt_from_chronological_order()
    {
        var refusals = GateFour.ReviewAudio([Child(1, "L-GIST", 60, 120), Child(2, "L-DET", 40, 50)]);

        Assert.Empty(refusals);
    }

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> skipping the ordering comparison for any
    /// pair that contains a gist item accepts a gist child parked at sequence 2 with an earlier span
    /// than item 1's, which is not the exception CNT-10 grants - the exception exists because the gist
    /// item sits first.
    /// </summary>
    [Fact]
    public void A_gist_item_out_of_order_at_sequence_two_is_refused()
    {
        var refusals = GateFour.ReviewAudio([Child(1, "L-DET", 40, 50), Child(2, "L-GIST", 10, 20)]);

        var refusal = Assert.Single(refusals);
        Assert.Equal(GateFourRules.AudioOrder, refusal.Rule);
    }

    /// <summary>Two detail spans that overlap have a negative gap; arithmetic on it must not pass.</summary>
    [Fact]
    public void Detail_spans_that_overlap_are_refused()
    {
        var refusals = GateFour.ReviewAudio([Child(1, "L-DET", 0, 30), Child(2, "L-DET", 20, 40)]);

        Assert.Contains(refusals, r => r.Rule == GateFourRules.AudioOrder);
    }

    /// <summary>
    /// A listening bundle's spans are seconds. <b>Which guard could this pass while being wrong:</b>
    /// reading character offsets as seconds reports an enormous gap and accepts, which is exactly what
    /// an import that copied a reading span onto audio would produce.
    /// </summary>
    [Fact]
    public void Audio_spans_in_characters_are_refused_rather_than_read_as_seconds()
    {
        var refusals = GateFour.ReviewAudio(
            [Child(1, "L-DET", 0, 1000, unit: "char"), Child(2, "L-DET", 2000, 3000, unit: "char")]);

        var refusal = Assert.Single(refusals);
        Assert.Equal(GateFourRules.AudioUnit, refusal.Rule);
    }

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> sorting by a duplicated sequence index
    /// makes the adjacency arbitrary, so one bundle is judged in one order and the same bundle in
    /// another on the next read. The indices are the bundle's shape and are checked before the pairs.
    /// </summary>
    [Fact]
    public void Duplicate_sequence_indices_are_refused_rather_than_sorted_arbitrarily()
    {
        var refusals = GateFour.ReviewAudio([Child(1, "L-DET", 0, 10), Child(1, "L-DET", 30, 40)]);

        var refusal = Assert.Single(refusals);
        Assert.Equal(GateFourRules.AudioSequence, refusal.Rule);
    }
}
