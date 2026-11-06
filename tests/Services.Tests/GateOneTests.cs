using System.Collections.Generic;
using BusinessObjects;

namespace Services.Tests;

/// <summary>
/// #194: gate one is a pure function, so every rule is checked here - no database, no fixture, no
/// clock. Each rule gets three inputs: one that breaks it, one that sits exactly on its boundary,
/// and one that is legal. The boundary case is the one that catches an off-by-one, and an off-by-one
/// here is either a real item refused or a real defect waved through.
/// <para>
/// The legal fixture below is a reading item a reviewer would pass: four three-word noun phrases
/// eight or more character edits apart, key A, a rationale per option and an explanation. Every test
/// changes exactly one thing from it, so a refusal names the change and nothing else.
/// </para>
/// </summary>
public sealed class GateOneTests
{
    private static GateOneItem Legal(
        string stem = "What must every visitor do at the gate?",
        string? stimulus = "Every visitor must sign the night ledger before entering the depot.",
        string? a = "The night ledger.",
        string? b = "The depot shutter.",
        string? c = "The depot supervisor.",
        string? d = "The office door.",
        string? key = "A",
        string? explanation = "The notice says every visitor signs the night ledger.",
        string? rationaleA = "Correct: the notice names the ledger.",
        string? rationaleB = "The passage never mentions a shutter.",
        string? rationaleC = "No supervisor appears in the passage.",
        string? rationaleD = "A door is not what the notice requires.") =>
        new(stem, stimulus, a, b, c, d, key, explanation, rationaleA, rationaleB, rationaleC, rationaleD);

    /// <summary>A cell with no keys yet, so no balance rule can bite.</summary>
    private static GateOneCell Empty => default;

    private static GateOneRefusal? Refusal(GateOneVerdict verdict, string rule) =>
        verdict.Refusals.FirstOrDefault(r => r.Rule == rule);

    private static string Rules(GateOneVerdict verdict) =>
        string.Join(", ", verdict.Refusals.Select(r => r.Rule));

    // -----------------------------------------------------------------------------------------
    // four options, and exactly one key
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void An_item_with_three_options_is_refused_by_the_four_option_rule()
    {
        var verdict = GateOne.Review(Legal(b: null), Empty);

        var refusal = Refusal(verdict, GateOneRules.FourOptions);
        Assert.NotNull(refusal);
        Assert.Contains("four", refusal!.Message, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3", refusal.Message, System.StringComparison.Ordinal);
        Assert.False(verdict.Accepted);
    }

    /// <summary>The boundary is the legal shape: four options with one key is accepted.</summary>
    [Fact]
    public void Four_options_and_one_key_are_accepted()
    {
        var verdict = GateOne.Review(Legal(), Empty);

        Assert.True(verdict.Accepted, Rules(verdict));
        Assert.Empty(verdict.Refusals);
    }

    /// <summary>
    /// Four options and no key is not "four valid options": it is an item nobody can score, and it
    /// is refused by a rule of its own rather than by the option-count rule.
    /// </summary>
    [Fact]
    public void Four_options_with_no_key_are_refused_by_the_key_rule()
    {
        var verdict = GateOne.Review(Legal(key: null), Empty);

        var refusal = Refusal(verdict, GateOneRules.MissingKey);
        Assert.NotNull(refusal);
        Assert.Contains("key", refusal!.Message, System.StringComparison.OrdinalIgnoreCase);
        Assert.Null(Refusal(verdict, GateOneRules.FourOptions));
    }

    /// <summary>
    /// A productive task - no options at all - is not judged by the multiple-choice rules. Its
    /// explanation is still required, and the last test here covers that.
    /// </summary>
    [Fact]
    public void A_productive_task_is_not_judged_by_the_option_rules()
    {
        var verdict = GateOne.Review(
            Legal(a: null, b: null, c: null, d: null, key: null) with
            {
                RationaleA = null, RationaleB = null, RationaleC = null, RationaleD = null,
            },
            Empty);

        Assert.True(verdict.Accepted, Rules(verdict));
    }

    // -----------------------------------------------------------------------------------------
    // pairwise Levenshtein distance > 2
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Two edits is the largest illegal distance, and it is refused: D17 says "greater than 2".
    /// "ledger"/"ledxor" differ by exactly two substitutions.
    /// </summary>
    [Fact]
    public void Two_options_two_edits_apart_are_refused()
    {
        var verdict = GateOne.Review(Legal(b: "The night ledxor."), Empty);

        var refusal = Refusal(verdict, GateOneRules.OptionsTooSimilar);
        Assert.NotNull(refusal);
        Assert.Contains("A", refusal!.Message, System.StringComparison.Ordinal);
        Assert.Contains("B", refusal.Message, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The boundary on the other side: three edits is the smallest legal distance, so it is accepted.
    /// "ledger"/"league" is three substitutions.
    /// </summary>
    [Fact]
    public void Two_options_three_edits_apart_are_accepted()
    {
        var verdict = GateOne.Review(Legal(b: "The night league."), Empty);

        Assert.Null(Refusal(verdict, GateOneRules.OptionsTooSimilar));
    }

    /// <summary>
    /// Every pair is compared, not the neighbouring ones. Here A and D - the two ends of the list -
    /// are the pair that is too close.
    /// </summary>
    [Fact]
    public void The_pair_checked_is_every_pair_not_only_neighbours()
    {
        var verdict = GateOne.Review(Legal(a: "The night ledger.", d: "The night ledxor."), Empty);

        var refusal = Refusal(verdict, GateOneRules.OptionsTooSimilar);
        Assert.NotNull(refusal);
        Assert.Contains("A", refusal!.Message, System.StringComparison.Ordinal);
        Assert.Contains("D", refusal.Message, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Case is not a difference. Two options that differ only in capitals are one option written
    /// twice, and a case-sensitive distance calls them distinct.
    /// </summary>
    [Fact]
    public void Options_that_differ_only_in_case_are_refused()
    {
        var verdict = GateOne.Review(Legal(b: "THE NIGHT LEDGER."), Empty);

        Assert.NotNull(Refusal(verdict, GateOneRules.OptionsTooSimilar));
    }

    // -----------------------------------------------------------------------------------------
    // no meta-options
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("All of the above.")]
    [InlineData("None of the above")]
    [InlineData("all of the above")]
    [InlineData("All of the above options.")]
    [InlineData("Neither A nor B")]
    [InlineData("neither a nor d.")]
    [InlineData("Both B and C")]
    [InlineData("Either C or D")]
    [InlineData("A and B")]
    public void A_meta_option_is_refused_by_name(string metaOption)
    {
        var verdict = GateOne.Review(Legal(d: metaOption), Empty);

        var refusal = Refusal(verdict, GateOneRules.MetaOption);
        Assert.NotNull(refusal);
        Assert.Contains("meta-option", refusal!.Message, System.StringComparison.OrdinalIgnoreCase);
        // Naming the option is what makes the refusal actionable.
        Assert.Contains("D", refusal.Message, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The trap in this rule: a substring match on "all", "above", "both" or "neither" refuses real
    /// distractors. Each of these is a legitimate option and must be accepted.
    /// </summary>
    [Theory]
    [InlineData("All staff must sign in.")]
    [InlineData("All of the visitors must sign in.")]
    [InlineData("None of the equipment was damaged.")]
    [InlineData("Both departments agreed to the schedule.")]
    [InlineData("Neither office reported a delay.")]
    [InlineData("The above information is not required.")]
    [InlineData("Above all, the ledger must be signed.")]
    public void An_option_that_merely_contains_a_meta_word_is_accepted(string option)
    {
        var verdict = GateOne.Review(Legal(d: option), Empty);

        Assert.Null(Refusal(verdict, GateOneRules.MetaOption));
    }

    // -----------------------------------------------------------------------------------------
    // the key's word count within 25% of the distractor mean
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Distractors of 4, 4 and 4 words (mean 4); a 7-word key is 75% over and refused.
    /// </summary>
    [Fact]
    public void A_key_whose_word_count_is_well_off_the_distractor_mean_is_refused()
    {
        var verdict = GateOne.Review(
            Legal(a: "The office door beside the depot gate."),
            Empty);

        var refusal = Refusal(verdict, GateOneRules.KeyLengthParity);
        Assert.NotNull(refusal);
        Assert.Contains("7", refusal!.Message, System.StringComparison.Ordinal);
        Assert.Contains("25", refusal.Message, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The boundary is exactly 25%, which is inside the tolerance: distractors of 4 words and a
    /// 5-word key. A rule written as "&lt; 25%" refuses this legal item.
    /// </summary>
    [Fact]
    public void A_key_exactly_25_percent_off_the_mean_is_accepted()
    {
        var verdict = GateOne.Review(
            Legal(
                a: "The office door and key.",
                b: "The depot shutter board.",
                c: "The depot supervisor note.",
                d: "The night ledger book."),
            Empty);

        Assert.Null(Refusal(verdict, GateOneRules.KeyLengthParity));
    }

    /// <summary>
    /// One word further is 50% over and refused, which pins the other side of the same boundary.
    /// </summary>
    [Fact]
    public void A_key_just_over_25_percent_is_refused()
    {
        var verdict = GateOne.Review(
            Legal(a: "At six tonight.", b: "Six tonight.", c: "Seven tonight.", d: "Eight tonight."),
            Empty);

        Assert.NotNull(Refusal(verdict, GateOneRules.KeyLengthParity));
    }

    /// <summary>
    /// Four one-word options cannot deviate: percentage parity on short options must not fire just
    /// because the absolute difference is small.
    /// </summary>
    [Fact]
    public void One_word_options_with_a_one_word_key_are_accepted()
    {
        var verdict = GateOne.Review(
            Legal(a: "ledger", b: "shutter", c: "supervisor", d: "office"),
            Empty);

        Assert.Null(Refusal(verdict, GateOneRules.KeyLengthParity));
    }

    // -----------------------------------------------------------------------------------------
    // part-of-speech parallelism
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Three noun phrases and one prepositional phrase: the odd one out is the shape of the answer,
    /// so it is refused and the odd option is named.
    /// </summary>
    [Fact]
    public void An_option_of_a_different_grammatical_shape_is_refused()
    {
        var verdict = GateOne.Review(Legal(d: "At six o'clock."), Empty);

        var refusal = Refusal(verdict, GateOneRules.PartOfSpeechParallelism);
        Assert.NotNull(refusal);
        Assert.Contains("parallel", refusal!.Message, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("D", refusal.Message, System.StringComparison.Ordinal);
    }

    /// <summary>Four noun phrases are parallel; nothing is refused.</summary>
    [Fact]
    public void Four_noun_phrases_are_accepted()
    {
        var verdict = GateOne.Review(
            Legal(a: "The night ledger.", b: "A depot shutter.", c: "The depot supervisor.", d: "The office door."),
            Empty);

        Assert.Null(Refusal(verdict, GateOneRules.PartOfSpeechParallelism));
    }

    /// <summary>
    /// The input that a guessing tagger gets wrong: a bare verb phrase. Its grammatical shape is not
    /// decidable without a tagger, so the gate reports that it did not judge rather than refusing
    /// real content - and the item is still accepted.
    /// </summary>
    [Fact]
    public void An_option_whose_shape_cannot_be_decided_is_reported_not_refused()
    {
        var verdict = GateOne.Review(Legal(d: "Leave a note."), Empty);

        Assert.Null(Refusal(verdict, GateOneRules.PartOfSpeechParallelism));
        Assert.True(verdict.Accepted, Rules(verdict));
        var notice = Assert.Single(
            verdict.Notices,
            n => n.Rule == GateOneRules.PartOfSpeechParallelism);
        Assert.Contains("D", notice.Message, System.StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------------------------
    // key balance between 20% and 30% per letter within a cell
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The input that looks like an imbalance and is not: an eleventh key cannot be distributed so
    /// that every letter stays inside the band, so no key choice could fix it. Reported, not refused.
    /// </summary>
    [Fact]
    public void A_key_is_not_refused_in_a_cell_size_that_cannot_be_balanced()
    {
        var verdict = GateOne.Review(Legal(key: "A"), new GateOneCell(A: 3, B: 3, C: 2, D: 2));

        Assert.Null(Refusal(verdict, GateOneRules.KeyCellBalance));
        Assert.True(verdict.Accepted, Rules(verdict));
        Assert.Contains(
            verdict.Notices,
            n => n.Rule == GateOneRules.KeyCellBalance
                 && n.Message.Contains("cannot be judged", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// The genuine refusal: nine keys at A3/B2/C2/D2, and keying this one A makes A 40% of ten. The
    /// existing ten-key shape is balanceable, so this one choice is what breaks it - and the refusal
    /// says which letter would fix it.
    /// </summary>
    [Fact]
    public void A_key_that_overweights_a_letter_in_a_balanceable_cell_is_refused()
    {
        var verdict = GateOne.Review(Legal(key: "A"), new GateOneCell(A: 3, B: 2, C: 2, D: 2));

        var refusal = Refusal(verdict, GateOneRules.KeyCellBalance);
        Assert.NotNull(refusal);
        Assert.Contains("A", refusal!.Message, System.StringComparison.Ordinal);
        Assert.Contains("B", refusal.Message, System.StringComparison.Ordinal);
        Assert.Contains("30", refusal.Message, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The boundary: exactly 30% is inside the band. Keying A into a cell of nine makes A 3 of 10.
    /// A rule written as "&lt; 30%" refuses this legal item.
    /// </summary>
    [Fact]
    public void A_cell_at_exactly_thirty_percent_is_accepted()
    {
        var verdict = GateOne.Review(Legal(key: "A"), new GateOneCell(A: 2, B: 2, C: 2, D: 3));

        Assert.Null(Refusal(verdict, GateOneRules.KeyCellBalance));
    }

    /// <summary>
    /// A cell that is already outside the band whichever letter this item takes is reported, not
    /// refused: the author cannot see the cell, and refusing them for its history is a false refusal.
    /// </summary>
    [Fact]
    public void A_key_in_a_cell_broken_by_its_history_is_reported_not_refused()
    {
        var verdict = GateOne.Review(Legal(key: "A"), new GateOneCell(A: 4, B: 3, C: 2, D: 0));

        Assert.Null(Refusal(verdict, GateOneRules.KeyCellBalance));
        Assert.Contains(verdict.Notices, n => n.Rule == GateOneRules.KeyCellBalance);
    }

    /// <summary>
    /// The half of the rule no item can fix. D holds one of ten keys - 10%, under the floor - and the
    /// cell reports it rather than accepting it silently.
    /// </summary>
    [Fact]
    public void An_underweighted_letter_is_reported_by_the_cell()
    {
        var notices = GateOne.ReviewCell(new GateOneCell(A: 3, B: 3, C: 3, D: 1));

        var notice = Assert.Single(notices);
        Assert.Equal(GateOneRules.KeyCellBalance, notice.Rule);
        Assert.Contains("D", notice.Message, System.StringComparison.Ordinal);
        Assert.Contains("20", notice.Message, System.StringComparison.Ordinal);
    }

    /// <summary>Exactly 20% and exactly 30% are inside the band: nothing is reported.</summary>
    [Fact]
    public void A_cell_on_both_band_edges_reports_nothing()
    {
        Assert.Empty(GateOne.ReviewCell(new GateOneCell(A: 3, B: 3, C: 2, D: 2)));
    }

    /// <summary>
    /// A one-key cell has not had the chance to be balanced. It is reported as not yet assessable
    /// rather than as an imbalance, because an empty pool would otherwise be reported as defective.
    /// </summary>
    [Fact]
    public void A_cell_too_small_to_balance_is_reported_as_not_yet_assessable()
    {
        var notices = GateOne.ReviewCell(new GateOneCell(A: 1, B: 0, C: 0, D: 0));

        var notice = Assert.Single(notices);
        Assert.Equal(GateOneRules.KeyCellBalance, notice.Rule);
        Assert.Contains("not yet", notice.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An empty cell says nothing at all: nothing has been authored, so nothing is wrong.</summary>
    [Fact]
    public void An_empty_cell_reports_nothing()
    {
        Assert.Empty(GateOne.ReviewCell(Empty));
    }

    /// <summary>The counts are read off the stored labels, and a label that is not A-D is not a key.</summary>
    [Fact]
    public void The_cell_counts_only_real_key_labels()
    {
        var cell = GateOneCell.FromLabels(["A", "A", "B", "c", "D", null, "X", " "]);

        Assert.Equal(new GateOneCell(A: 2, B: 1, C: 1, D: 1), cell);
        Assert.Equal(5, cell.Total);
    }

    // -----------------------------------------------------------------------------------------
    // no unique content lemma in stem + key, absent from the stimulus and the distractors
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// The giveaway CNT-35 names: "permit" is in the stem and in the key and nowhere else, so a
    /// learner who reads neither the passage nor the options can still match stem to key.
    /// </summary>
    [Fact]
    public void A_content_word_shared_by_stem_and_key_but_absent_elsewhere_is_refused()
    {
        var verdict = GateOne.Review(
            Legal(
                stem: "Which permit must contractors renew?",
                stimulus: "Contractors must renew their licence every year.",
                a: "The safety permit.",
                b: "The licence copy.",
                c: "The insurance form.",
                d: "The training record."),
            Empty);

        var refusal = Refusal(verdict, GateOneRules.UniqueStemKeyLemma);
        Assert.NotNull(refusal);
        Assert.Contains("permit", refusal!.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The same shared word, but the stimulus carries it: the item is answerable from the passage
    /// and the word match proves nothing.
    /// </summary>
    [Fact]
    public void A_shared_content_word_present_in_the_stimulus_is_accepted()
    {
        var verdict = GateOne.Review(
            Legal(
                stem: "Which permit must contractors renew?",
                stimulus: "Contractors must renew their safety permit every year.",
                a: "The safety permit.",
                b: "The licence copy.",
                c: "The insurance form.",
                d: "The training record."),
            Empty);

        Assert.Null(Refusal(verdict, GateOneRules.UniqueStemKeyLemma));
    }

    /// <summary>
    /// The stimulus carries the shared word only in an inflected form: "permits" answers "permit". A
    /// raw-string rule refuses this real item.
    /// </summary>
    [Fact]
    public void An_inflected_form_of_the_shared_word_counts_as_present()
    {
        var verdict = GateOne.Review(
            Legal(
                stem: "Which permit must contractors renew?",
                stimulus: "Contractors renew their safety permits every year.",
                a: "The safety permit.",
                b: "The licence copy.",
                c: "The insurance form.",
                d: "The training record."),
            Empty);

        Assert.Null(Refusal(verdict, GateOneRules.UniqueStemKeyLemma));
    }

    /// <summary>
    /// The fold runs in both directions: the stem asks about "permits" and the key says "permit". A
    /// rule that folded only the stimulus would refuse this, and a rule that folded neither would too.
    /// </summary>
    [Fact]
    public void An_inflected_stem_still_matches_the_key()
    {
        var verdict = GateOne.Review(
            Legal(
                stem: "Which permits must contractors renew?",
                stimulus: "Contractors must renew their licence every year.",
                a: "The safety permit.",
                b: "The licence copy.",
                c: "The insurance form.",
                d: "The training record."),
            Empty);

        var refusal = Refusal(verdict, GateOneRules.UniqueStemKeyLemma);
        Assert.NotNull(refusal);
        Assert.Contains("permits", refusal!.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The word the stem and key share is a function word. A rule without a stopword list refuses
    /// this item; "the" identifies nothing.
    /// </summary>
    [Fact]
    public void A_shared_stopword_is_not_a_content_lemma()
    {
        var verdict = GateOne.Review(
            Legal(
                stem: "What does the visitor show?",
                stimulus: "Visitors show badges at reception.",
                a: "The badge.",
                b: "badge log",
                c: "reception desk",
                d: "sign in sheet"),
            Empty);

        Assert.Null(Refusal(verdict, GateOneRules.UniqueStemKeyLemma));
    }

    /// <summary>
    /// A stem word absent from the stimulus is not the defect - the defect is a word the <em>key</em>
    /// repeats. A rule that flagged any unexplained stem word would refuse half the pool.
    /// </summary>
    [Fact]
    public void A_stem_word_absent_from_the_stimulus_is_not_refused_unless_the_key_repeats_it()
    {
        var verdict = GateOne.Review(
            Legal(
                stem: "Which certificate must contractors display?",
                stimulus: "Workers display a badge at all times.",
                a: "The safety document.",
                b: "The licence copy.",
                c: "The insurance form.",
                d: "The training record."),
            Empty);

        Assert.Null(Refusal(verdict, GateOneRules.UniqueStemKeyLemma));
    }

    /// <summary>
    /// The gap that must be admitted rather than waived: an item with no stimulus text - a standalone
    /// cloze, or an audio item whose transcript is not carried - cannot be judged by this rule at all.
    /// Silence is not evidence, so it is reported and the item is still accepted.
    /// </summary>
    [Fact]
    public void An_item_with_no_stimulus_text_is_reported_not_refused()
    {
        var verdict = GateOne.Review(
            Legal(
                stem: "Contractors must renew their permit before Friday.",
                stimulus: null,
                a: "The safety permit.",
                b: "The depot closes.",
                c: "The shift ends.",
                d: "The office moves."),
            Empty);

        Assert.Null(Refusal(verdict, GateOneRules.UniqueStemKeyLemma));
        Assert.Contains(verdict.Notices, n => n.Rule == GateOneRules.UniqueStemKeyLemma);
    }

    // -----------------------------------------------------------------------------------------
    // a non-empty explanation and a rationale per option
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_item_without_an_explanation_is_refused(string? explanation)
    {
        var verdict = GateOne.Review(Legal(explanation: explanation), Empty);

        var refusal = Refusal(verdict, GateOneRules.Explanation);
        Assert.NotNull(refusal);
        Assert.Contains("explanation", refusal!.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_option_without_a_rationale_is_refused_and_the_option_is_named()
    {
        var verdict = GateOne.Review(Legal(rationaleC: "  "), Empty);

        var refusal = Refusal(verdict, GateOneRules.OptionRationale);
        Assert.NotNull(refusal);
        Assert.Contains("rationale", refusal!.Message, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("C", refusal.Message, System.StringComparison.Ordinal);
        Assert.DoesNotContain("A,", refusal.Message, System.StringComparison.Ordinal);
    }

    /// <summary>Four missing rationales are one refusal naming all four, not four refusals.</summary>
    [Fact]
    public void All_four_rationales_missing_is_one_refusal_naming_them()
    {
        var verdict = GateOne.Review(
            Legal(rationaleA: null, rationaleB: null, rationaleC: null, rationaleD: null),
            Empty);

        var refusal = Assert.Single(verdict.Refusals, r => r.Rule == GateOneRules.OptionRationale);
        foreach (var label in new[] { "A", "B", "C", "D" })
        {
            Assert.Contains(label, refusal.Message, System.StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Several rules can break at once, and every broken rule is reported: an author who fixes the
    /// first refusal and re-uploads should not discover the second one then.
    /// </summary>
    [Fact]
    public void Every_broken_rule_is_reported_not_only_the_first()
    {
        var verdict = GateOne.Review(
            Legal(explanation: null, rationaleD: null, b: "The night ledxor."),
            Empty);

        Assert.NotNull(Refusal(verdict, GateOneRules.Explanation));
        Assert.NotNull(Refusal(verdict, GateOneRules.OptionsTooSimilar));
        Assert.NotNull(Refusal(verdict, GateOneRules.OptionRationale));
    }
}
