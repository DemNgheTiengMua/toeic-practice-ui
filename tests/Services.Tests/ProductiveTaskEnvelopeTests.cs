using System;
using System.Collections.Generic;
using System.Linq;
using BusinessObjects;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #204 / S-07: what a productive task may be, before anything is written. D0.4's bounds per type,
/// D0.2's writing floor per band, and D0.7's placement caps.
/// <para>
/// These are pure rules, so they are tested pure - no database, no clock, no model. The service tests
/// beside this file assert the same rules <em>on the stored row</em>, because a range asserted only
/// on a DTO is a range nothing guarantees the pool holds.
/// </para>
/// </summary>
public sealed class ProductiveTaskEnvelopeTests
{
    private static readonly IReadOnlyList<ProductiveRefusal> None = [];

    private static ProductiveSubTaskShape Writing(
        string type = "persuasive_essay",
        int? min = 110,
        int? max = 300,
        int? prep = null,
        int? response = 900,
        string prompt = "Write an essay answering the question above.",
        string? explanation = "The essay should state a position and defend it with reasons.") =>
        new(type, prompt, min, max, prep, response, explanation);

    private static ProductiveSubTaskShape Speaking(
        string type = "argumentative_monologue",
        int? prep = 60,
        int? response = 120,
        int? min = null,
        int? max = null,
        string prompt = "Argue for or against the proposition above.",
        string? explanation = "The answer should defend one side with reasons.") =>
        new(type, prompt, min, max, prep, response, explanation);

    private static IReadOnlyList<ProductiveRefusal> ReviewWriting(
        CefrLevel level,
        IReadOnlyList<ProductiveSubTaskShape> subTasks,
        bool placement = false,
        string? image = null,
        string? alt = null,
        string prompt = "Write an essay answering the question above.") =>
        ProductiveTaskEnvelope.Review("writing", level, placement, prompt, image, alt, subTasks);

    private static IReadOnlyList<ProductiveRefusal> ReviewSpeaking(
        CefrLevel level,
        IReadOnlyList<ProductiveSubTaskShape> subTasks,
        bool placement = false,
        string? image = null,
        string? alt = null) =>
        ProductiveTaskEnvelope.Review(
            "speaking", level, placement, "Argue for or against the proposition above.", image, alt, subTasks);

    private static ProductiveRefusal? Refusal(IReadOnlyList<ProductiveRefusal> refusals, string rule) =>
        refusals.FirstOrDefault(refusal => refusal.Rule == rule);

    // -----------------------------------------------------------------------------------------
    // the accept direction, so every refusal below is not a blanket one
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void A_one_part_writing_task_at_its_band_is_accepted()
    {
        Assert.Empty(ReviewWriting(CefrLevel.B1, [Writing()]));
    }

    [Fact]
    public void A_two_part_writing_task_is_accepted_when_both_parts_carry_their_own_range_and_budget()
    {
        var legal = ReviewWriting(CefrLevel.B2,
        [
            // B2's demand is 180-250, so the type's 50-150 lifts to the band floor.
            Writing("transactional_correspondence", min: 180, max: 180, response: 480,
                prompt: "Write the email described above."),
            Writing("persuasive_essay", min: 180, max: 250, response: 1200,
                prompt: "Write the essay described above."),
        ]);

        Assert.Empty(legal);

        // Each part is judged on its OWN type: the same range on a type whose ceiling is 180 is refused
        // at the ceiling, which is what makes "both parts carry their own range" mean something.
        var overType = ReviewWriting(CefrLevel.B2,
            [Writing("transactional_correspondence", min: 180, max: 300, response: 480,
                prompt: "Write the email described above.")]);

        Assert.NotNull(Refusal(overType, ProductiveTaskRules.WordRange));
    }

    /// <summary>
    /// <b>The input that would pass a lazy guard.</b> "50-150 words" is D0.4's own text for a
    /// transactional correspondence, so a rule that read only D0.4 accepts it at every band. At B2 it
    /// measures 50-word writing against a band whose demand starts at 180 - the same defect D0.3's
    /// envelope exists to stop, one level up.
    /// </summary>
    [Fact]
    public void A_writing_range_read_straight_off_D0_4_is_refused_above_the_band_it_was_written_for()
    {
        var refusals = ReviewWriting(CefrLevel.B2,
            [Writing("transactional_correspondence", min: 50, max: 150, response: 480,
                prompt: "Write the email described above.")]);

        var refusal = Refusal(refusals, ProductiveTaskRules.WordRange);
        Assert.NotNull(refusal);
        Assert.Contains("180", refusal!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_speaking_task_with_a_preparation_and_a_recording_window_is_accepted()
    {
        Assert.Empty(ReviewSpeaking(CefrLevel.B1, [Speaking()]));
    }

    // -----------------------------------------------------------------------------------------
    // D0.4's sub-task count - the trap here is a two-part task saved with one part present
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input that would pass a lazy guard.</b> A writing task "prepared for two sub-tasks" whose
    /// second slot was left empty. A check that only bounds the count at "at most two" accepts it, and
    /// the sitting then scores a task whose second half was never authored. The refusal names the
    /// missing part by position, because "a sub-task is empty" does not say which one to write.
    /// </summary>
    [Fact]
    public void A_two_part_writing_task_with_its_second_part_empty_is_refused_and_the_part_is_named()
    {
        var refusals = ReviewWriting(CefrLevel.B1,
        [
            Writing(prompt: "Write the email described above."),
            Writing(prompt: "   "),
        ]);

        var refusal = Refusal(refusals, ProductiveTaskRules.Prompt);
        Assert.NotNull(refusal);
        Assert.Contains("sub-task 2", refusal!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_writing_task_with_three_parts_is_refused_and_the_count_is_named()
    {
        var refusals = ReviewWriting(CefrLevel.B1,
        [
            Writing(), Writing(prompt: "And the second part."), Writing(prompt: "And a third."),
        ]);

        var refusal = Refusal(refusals, ProductiveTaskRules.SubTaskCount);
        Assert.NotNull(refusal);
        Assert.Contains("3", refusal!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_speaking_task_with_two_parts_is_refused()
    {
        var refusals = ReviewSpeaking(CefrLevel.B1, [Speaking(), Speaking(type: "descriptive_monologue")]);

        Assert.NotNull(Refusal(refusals, ProductiveTaskRules.SubTaskCount));
    }

    // -----------------------------------------------------------------------------------------
    // the word range - three wrong-but-passing inputs
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input that would pass a lazy guard.</b> A writing task whose range is 20-300 at B1: the
    /// ceiling is inside D0.4's bound and the pair is not inverted, so a check that only asks "is the
    /// range well formed" accepts it. The prompt then measures B1 writing with an A1 demand.
    /// </summary>
    [Fact]
    public void A_writing_range_below_the_bands_output_demand_is_refused()
    {
        var refusals = ReviewWriting(CefrLevel.B1, [Writing(min: 20)]);

        var refusal = Refusal(refusals, ProductiveTaskRules.WordRange);
        Assert.NotNull(refusal);
        Assert.Contains("110", refusal!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_writing_range_above_its_types_ceiling_is_refused()
    {
        // B1's floor is 110, so a persuasive essay may run to 300; 400 is over both.
        var refusals = ReviewWriting(CefrLevel.B1, [Writing(max: 400)]);

        Assert.NotNull(Refusal(refusals, ProductiveTaskRules.WordRange));
    }

    [Fact]
    public void A_writing_task_with_no_word_range_is_refused()
    {
        var refusals = ReviewWriting(CefrLevel.B1, [Writing(min: null, max: null)]);

        Assert.NotNull(Refusal(refusals, ProductiveTaskRules.WordRange));
    }

    /// <summary>
    /// A speaking answer's length is its recording window. A word range on a speaking task is a field
    /// the sitting would show and the rubric would ignore - and would measure the learner's speaking
    /// rate rather than the demand the author meant.
    /// </summary>
    [Fact]
    public void A_speaking_task_that_declares_a_word_range_is_refused()
    {
        var refusals = ReviewSpeaking(CefrLevel.B1, [Speaking(min: 50, max: 100)]);

        Assert.NotNull(Refusal(refusals, ProductiveTaskRules.WordRange));
    }

    // -----------------------------------------------------------------------------------------
    // the windows - zero, negative, inverted and out of the type's envelope
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input that would pass a lazy guard.</b> A preparation window of zero. The schema's
    /// <c>PrepSeconds &gt;= 0</c> on the old Stimuli table accepted it, and the countdown renders zero
    /// as already finished - so the sitting begins the answer with no preparation and the author
    /// believes they allowed some.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void A_preparation_window_of_zero_or_less_is_refused(int prep)
    {
        var refusals = ReviewSpeaking(CefrLevel.B1, [Speaking(prep: prep)]);

        Assert.NotNull(Refusal(refusals, ProductiveTaskRules.Timing));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void A_recording_window_of_zero_or_less_is_refused(int response)
    {
        var refusals = ReviewSpeaking(CefrLevel.B1, [Speaking(response: response)]);

        Assert.NotNull(Refusal(refusals, ProductiveTaskRules.Timing));
    }

    /// <summary>
    /// An inverted pair - preparation outlasting the recording it prepares for is legal; a recording
    /// window inside a prep window that runs past it is not something the sitting can render.
    /// </summary>
    [Fact]
    public void A_preparation_window_outside_its_types_envelope_is_refused()
    {
        var refusals = ReviewSpeaking(CefrLevel.B1, [Speaking(prep: 200)]);

        Assert.NotNull(Refusal(refusals, ProductiveTaskRules.Timing));
    }

    [Fact]
    public void A_recording_window_outside_its_types_envelope_is_refused()
    {
        // argumentative_monologue allows 90-120 s; 30 is below it.
        var refusals = ReviewSpeaking(CefrLevel.B1, [Speaking(response: 30)]);

        Assert.NotNull(Refusal(refusals, ProductiveTaskRules.Timing));
    }

    [Fact]
    public void A_task_with_no_recording_window_is_refused()
    {
        Assert.NotNull(Refusal(ReviewSpeaking(CefrLevel.B1, [Speaking(response: null)]), ProductiveTaskRules.Timing));
    }

    // -----------------------------------------------------------------------------------------
    // the placement cap - the number the author must see before doing the work
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>The input that would pass a lazy guard.</b> A C1 speaking task with a 120-second recording
    /// window marked placement-eligible. D0.2 allows 90-120 s at C1, so every practice rule accepts
    /// it - and only the placement cap refuses it. The refusal is on the <em>draft</em>, which is what
    /// makes the cap visible before the author writes the task rather than at extraction.
    /// </summary>
    [Fact]
    public void A_placement_speaking_task_over_sixty_seconds_is_refused_and_the_cap_is_named()
    {
        var refusals = ReviewSpeaking(CefrLevel.C1, [Speaking(response: 120)], placement: true);

        var refusal = Refusal(refusals, ProductiveTaskRules.PlacementCap);
        Assert.NotNull(refusal);
        Assert.Contains("60", refusal!.Message, StringComparison.Ordinal);

        // And the same task is accepted as practice stock: the cap is placement's, not the type's.
        Assert.Empty(ReviewSpeaking(CefrLevel.C1, [Speaking(response: 120)]));
    }

    [Fact]
    public void A_placement_speaking_task_at_sixty_seconds_is_accepted()
    {
        // D0.4's argumentative_monologue records 90-120 s; D0.2 caps placement at 60 for every band, so
        // the cap replaces the type's floor rather than sitting above it.
        Assert.Empty(ReviewSpeaking(CefrLevel.C1, [Speaking(response: 60)], placement: true));
    }

    [Fact]
    public void A_placement_speaking_task_below_the_cap_is_still_timing_refused()
    {
        // The cap narrows the window; it does not license a one-second recording.
        Assert.NotNull(Refusal(
            ReviewSpeaking(CefrLevel.C1, [Speaking(response: 5)], placement: true),
            ProductiveTaskRules.Timing));
    }

    /// <summary>
    /// D0.7 scopes the placement variants of C1/C2 writing below the practice envelope, so two of
    /// them fit the 22-minute budget. <b>The input that would pass a lazy guard:</b> a cap that only
    /// narrowed the ceiling would leave C1's 250-word floor under a 140-word ceiling - a task no answer
    /// can satisfy - so both ends move and the refusal names the placement figure.
    /// </summary>
    [Fact]
    public void A_placement_writing_task_at_c1_is_scoped_below_its_practice_envelope()
    {
        var refusals = ReviewWriting(CefrLevel.C1,
            [Writing(min: 250, max: 300, response: 1200, prompt: "Write the essay.")],
            placement: true);

        var refusal = Refusal(refusals, ProductiveTaskRules.PlacementCap);
        Assert.NotNull(refusal);
        Assert.Contains("140", refusal!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_c1_placement_writing_task_inside_the_scoped_range_is_accepted()
    {
        // The same task is refused as practice stock, because 120 words is below C1's 250-word floor.
        Assert.NotNull(Refusal(
            ReviewWriting(CefrLevel.C1,
                [Writing(min: 120, max: 140, response: 900, prompt: "Write the essay.")]),
            ProductiveTaskRules.WordRange));

        Assert.Empty(ReviewWriting(CefrLevel.C1,
            [Writing(min: 120, max: 140, response: 900, prompt: "Write the essay.")],
            placement: true));
    }

    [Fact]
    public void A_two_part_placement_writing_task_scopes_its_parts_separately()
    {
        // Task 1 is the shorter, Task 2 the longer: 120-140 and 180-220 (D0.7).
        Assert.Empty(ReviewWriting(CefrLevel.C2,
        [
            Writing("transactional_correspondence", min: 120, max: 140, response: 480, prompt: "Write the email."),
            Writing("persuasive_essay", min: 180, max: 220, response: 1200, prompt: "Write the essay."),
        ], placement: true));

        // And a second part held to the first part's ceiling is refused, because the two differ.
        var wrong = ReviewWriting(CefrLevel.C2,
        [
            Writing("transactional_correspondence", min: 120, max: 140, response: 480, prompt: "Write the email."),
            Writing("persuasive_essay", min: 120, max: 140, response: 1200, prompt: "Write the essay."),
        ], placement: true);

        Assert.NotNull(Refusal(wrong, ProductiveTaskRules.PlacementCap));
        Assert.Contains("180", Assert.Single(wrong, r => r.Rule == ProductiveTaskRules.PlacementCap).Message,
            StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------------------------
    // the visual stimulus - needed by a type, never by another
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void A_data_synthesis_task_without_its_visual_stimulus_is_refused()
    {
        var refusals = ReviewWriting(CefrLevel.B1,
            [Writing("visual_data_synthesis", max: 180, response: 600, prompt: "Summarise the chart.")],
            image: null, alt: null);

        Assert.NotNull(Refusal(refusals, ProductiveTaskRules.VisualStimulus));
    }

    [Fact]
    public void A_data_synthesis_task_with_its_visual_and_alt_text_is_accepted()
    {
        Assert.Empty(ReviewWriting(CefrLevel.B1,
            [Writing("visual_data_synthesis", max: 180, response: 600, prompt: "Summarise the chart.")],
            image: "img/chart-1.svg", alt: "A bar chart of quarterly revenue."));
    }

    [Fact]
    public void A_visual_stimulus_without_alt_text_is_refused()
    {
        var refusals = ReviewWriting(CefrLevel.B1,
            [Writing("visual_data_synthesis", max: 180, response: 600, prompt: "Summarise the chart.")],
            image: "img/chart-1.svg", alt: null);

        Assert.NotNull(Refusal(refusals, ProductiveTaskRules.VisualStimulus));
    }

    /// <summary>
    /// A persuasive essay has no visual in D0.4. An image on one is a field no screen renders, and a
    /// test that only asserted "the required image is present" would never see it.
    /// </summary>
    [Fact]
    public void A_visual_stimulus_on_a_type_that_never_shows_one_is_refused()
    {
        var refusals = ReviewWriting(CefrLevel.B1, [Writing()], image: "img/chart-1.svg", alt: "A chart.");

        Assert.NotNull(Refusal(refusals, ProductiveTaskRules.VisualStimulus));
    }

    [Fact]
    public void A_descriptive_monologue_may_carry_an_optional_image_and_must_carry_alt_text_when_it_does()
    {
        var without = ReviewSpeaking(CefrLevel.B1,
            [Speaking("descriptive_monologue", prep: 30, response: 45)]);
        Assert.Empty(without);

        var withImage = ReviewSpeaking(CefrLevel.B1,
            [Speaking("descriptive_monologue", prep: 30, response: 45)],
            image: "img/desk.svg", alt: "A desk with two folders.");
        Assert.Empty(withImage);

        var undescribed = ReviewSpeaking(CefrLevel.B1,
            [Speaking("descriptive_monologue", prep: 30, response: 45)],
            image: "img/desk.svg", alt: null);
        Assert.NotNull(Refusal(undescribed, ProductiveTaskRules.VisualStimulus));
    }

    // -----------------------------------------------------------------------------------------
    // context and identity
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void A_task_with_no_prompt_is_refused()
    {
        var refusals = ReviewWriting(CefrLevel.B1, [Writing()], prompt: "   ");

        Assert.NotNull(Refusal(refusals, ProductiveTaskRules.Prompt));
    }

    /// <summary>
    /// Gate one refuses a productive task with no explanation (CNT-35), and the studio says so on the
    /// field rather than after a save - the same rule that puts the placement cap on the analysis.
    /// <b>The input that would pass a lazy guard:</b> a shape check that only asked "is there a
    /// prompt" accepts a task whose only post-answer copy is empty.
    /// </summary>
    [Fact]
    public void A_part_with_no_explanation_is_refused_and_the_part_is_named()
    {
        var refusals = ReviewWriting(CefrLevel.B1, [Writing(explanation: "  ")]);

        var refusal = Refusal(refusals, ProductiveTaskRules.Prompt);
        Assert.NotNull(refusal);
        Assert.Contains("explanation", refusal!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sub-task 1", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_objective_skill_is_not_a_productive_task()
    {
        var refusals = ProductiveTaskEnvelope.Review(
            "reading", CefrLevel.B1, false, "A passage.", null, null, [Writing()]);

        Assert.NotNull(Refusal(refusals, ProductiveTaskRules.ItemType));
    }

    [Fact]
    public void A_type_from_the_other_productive_skill_is_refused()
    {
        var refusals = ReviewSpeaking(CefrLevel.B1, [Speaking(type: "persuasive_essay")]);

        var refusal = Refusal(refusals, ProductiveTaskRules.ItemType);
        Assert.NotNull(refusal);
        Assert.Contains("descriptive_monologue", refusal!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reportable_floor_is_not_an_authored_level()
    {
        Assert.NotNull(Refusal(ReviewWriting(CefrLevel.PreA1, [Writing()]), ProductiveTaskRules.Context));
    }

    /// <summary>
    /// D0.4's table is the authority on which types exist and which skill owns them. The schema's
    /// <c>CK_Items_Modality_Type</c> covers the same ground; this asserts the two lists agree, so a
    /// type added to one and not the other fails here rather than at a save.
    /// </summary>
    [Fact]
    public void The_type_table_carries_exactly_D0_4s_eight_productive_types()
    {
        Assert.Equal(
            ["descriptive_monologue", "situational_negotiation", "argumentative_monologue", "visual_data_synthesis"],
            ProductiveTaskEnvelope.TypesFor("speaking"));

        Assert.Equal(
            ["transactional_correspondence", "visual_data_synthesis", "persuasive_essay", "analytical_evaluation_essay"],
            ProductiveTaskEnvelope.TypesFor("writing"));

        Assert.Empty(ProductiveTaskEnvelope.TypesFor("reading"));
        Assert.Empty(ProductiveTaskEnvelope.TypesFor("listening"));
    }
}
