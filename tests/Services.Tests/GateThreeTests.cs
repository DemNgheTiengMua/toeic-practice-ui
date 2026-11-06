using System;
using System.Collections.Generic;
using BusinessObjects;

namespace Services.Tests;

/// <summary>
/// #196: gate three is a pure function over three already-solved distributions, so every threshold is
/// checked here without a provider, a database or a cent of spend.
/// <para>
/// The trap this file is built against is <b>agreeing by accident</b>: two verifiers that are one
/// model, or a confidence check written against a number that is always 1.0. Three devices make that
/// trap fail rather than pass:
/// <list type="number">
/// <item>every threshold test drives the <em>other</em> checks to a state where they cannot fire, so
/// the refusal that appears is the one under test - a test whose key is the most likely option and
/// whose distractors are tiny can only fail on confidence;</item>
/// <item>the boundary is tested on both sides, because <c>&gt;=</c> where <c>&gt;</c> was specified
/// (and the reverse) is the whole difference between the spec and a guard that looks like it;</item>
/// <item>the legal fixture is accepted, so a gate that refuses everything is not mistaken for a gate
/// that works.</item>
/// </list>
/// </para>
/// </summary>
public sealed class GateThreeTests
{
    private const string Author = "claude";
    private const string VerifierA = "gpt";
    private const string VerifierB = "gemini";

    /// <summary>
    /// A distribution written as four arguments, so a test reads as "this is what the model said"
    /// rather than as a dictionary literal.
    /// </summary>
    private static GateThreeSolve Solve(string family, decimal a, decimal b, decimal c, decimal d) =>
        new(family, $"{family}-model", new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["A"] = a, ["B"] = b, ["C"] = c, ["D"] = d,
        });

    /// <summary>
    /// A confident, agreeing solve on <paramref name="key"/>: 0.90 on it, the three distractors at
    /// 0.05 or less. Keyed rather than hard-coded to A, because a fixture that only ever agrees on A
    /// silently couples every test to the default key - which is how one of these tests was written
    /// to assert dissenting on B while the gate was reading A.
    /// </summary>
    private static GateThreeSolve AgreesOn(string family, string key = "A")
    {
        decimal a = 0.02m, b = 0.03m, c = 0.05m, d = 0.90m;
        return key switch
        {
            "A" => Solve(family, d, c, b, a),
            "B" => Solve(family, a, d, c, b),
            "C" => Solve(family, b, a, d, c),
            "D" => Solve(family, c, b, a, d),
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "not one of A-D"),
        };
    }

    private static GateThreeSolve Agrees(string family) => AgreesOn(family);

    /// <summary>A solve that finds nothing: the ablation's passing shape.</summary>
    private static GateThreeSolve NoIdea(string family) => Solve(family, 0.25m, 0.25m, 0.25m, 0.25m);

    private static GateThreeVerdict Review(
        GateThreeSolve? one = null,
        GateThreeSolve? two = null,
        GateThreeSolve? blind = null,
        string author = Author,
        string key = "A") =>
        GateThree.Review(
            author,
            key,
            one ?? Agrees(VerifierA),
            two ?? Agrees(VerifierB),
            blind ?? NoIdea(VerifierB));

    private static GateThreeRefusal? Refusal(GateThreeVerdict verdict, string rule) =>
        verdict.Refusals.FirstOrDefault(r => r.Rule == rule);

    private static string Rules(GateThreeVerdict verdict) =>
        string.Join(", ", verdict.Refusals.Select(r => r.Rule));

    // -----------------------------------------------------------------------------------------
    // the legal shape
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Three distinct families, two confident agreeing verifiers, and a blind solver that found
    /// nothing. This is what an item has to look like to be verified, and the gate must say yes to it:
    /// a gate that refuses everything also "catches" every bad item.
    /// </summary>
    [Fact]
    public void Two_confident_verifiers_and_a_blind_solver_that_finds_nothing_are_accepted()
    {
        var verdict = Review();

        Assert.True(verdict.Accepted, Rules(verdict));
        Assert.Empty(verdict.Refusals);
    }

    // -----------------------------------------------------------------------------------------
    // the key must be selected at or above 0.85
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// 0.85 exactly is inside the rule. This is the half of the boundary that an off-by-one written
    /// <c>&gt; 0.85</c> would refuse - a real item lost to a guard that reads right.
    /// </summary>
    [Fact]
    public void A_key_confidence_of_exactly_the_threshold_is_accepted()
    {
        var verdict = Review(one: Solve(VerifierA, 0.85m, 0.05m, 0.05m, 0.05m));

        Assert.True(verdict.Accepted, Rules(verdict));
    }

    /// <summary>
    /// 0.84 with the key still the most likely option and every distractor tiny. <b>Which guard could
    /// this input pass while being wrong:</b> a confidence check that never runs because the caller
    /// passes the key in and the check reads "did the model pick the key", or one written
    /// <c>&gt;=</c> against a value that is always 1.0. Here the only rule that can fire is
    /// confidence, so the refusal that appears is the confidence one and nothing else.
    /// </summary>
    [Fact]
    public void A_key_confidence_just_below_the_threshold_is_refused_by_the_confidence_rule()
    {
        var verdict = Review(one: Solve(VerifierA, 0.84m, 0.06m, 0.05m, 0.05m));

        var refusal = Refusal(verdict, GateThreeRules.KeyConfidence);
        Assert.NotNull(refusal);
        Assert.Equal(GateThreeRules.KeyConfidence, Assert.Single(verdict.Refusals).Rule);
        Assert.False(verdict.Accepted);

        // The message has to point at the model and at the number, or the author cannot tell a weak
        // solver from a broken one.
        Assert.Contains("verifier 1", refusal!.Message, StringComparison.Ordinal);
        Assert.Contains(VerifierA, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("84", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("85", refusal.Message, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------------------------
    // every distractor must be strictly below 0.15
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// 0.15 exactly is <em>outside</em> the rule, because D17 says "less than 0.15". A guard written
    /// <c>&gt; 0.15</c> accepts a distractor the spec rejects, and this value is the only one that can
    /// tell the two apart.
    /// </summary>
    [Fact]
    public void A_distractor_at_exactly_the_ceiling_is_refused()
    {
        // Key A 0.85 is confident and the most likely, so the confidence and dissent rules cannot fire.
        var verdict = Review(one: Solve(VerifierA, 0.85m, 0.15m, 0.00m, 0.00m));

        var refusal = Refusal(verdict, GateThreeRules.DistractorCeiling);
        Assert.NotNull(refusal);
        Assert.Equal(GateThreeRules.DistractorCeiling, Assert.Single(verdict.Refusals).Rule);
        Assert.Contains("verifier 1", refusal!.Message, StringComparison.Ordinal);
        Assert.Contains("B", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("15", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Just under the ceiling is legal, so the rule is a threshold and not "any distractor weight at
    /// all". Without this, <c>&gt;= 0</c> would pass the test above.
    /// </summary>
    [Fact]
    public void A_distractor_just_below_the_ceiling_is_accepted()
    {
        var verdict = Review(one: Solve(VerifierA, 0.85m, 0.149m, 0.001m, 0.00m));

        Assert.True(verdict.Accepted, Rules(verdict));
    }

    /// <summary>
    /// The ceiling is checked on <em>every</em> distractor, not only the runner-up. Here B is
    /// comfortably low and it is D, the last option, that is too heavy: a rule that inspects
    /// <c>OrderByDescending().Skip(1).First()</c> reads the same as a rule over all three until this
    /// input arrives.
    /// </summary>
    [Fact]
    public void The_ceiling_applies_to_the_last_distractor_and_not_only_the_runner_up()
    {
        var verdict = Review(one: Solve(VerifierA, 0.85m, 0.00m, 0.00m, 0.15m));

        var refusal = Refusal(verdict, GateThreeRules.DistractorCeiling);
        Assert.NotNull(refusal);
        Assert.Contains("D", refusal!.Message, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------------------------
    // a verifier that disagrees, named
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Both solvers are confident, so every numeric threshold is met - and the second one simply
    /// chose a different option. <b>Which guard could this pass while being wrong:</b> a dissent check
    /// that reads verifier one only, which is the natural way to write it when the same code serves
    /// both. The refusal must name <c>verifier 2</c>: a bare "verification failed" leaves the author
    /// unable to tell which model to re-run.
    /// </summary>
    [Fact]
    public void The_second_verifier_dissenting_refuses_the_item_and_is_named()
    {
        // Both are lopsided towards a single option, so only "which option" differs.
        var verdict = Review(
            one: Agrees(VerifierA),
            two: Solve(VerifierB, 0.05m, 0.90m, 0.03m, 0.02m));

        var dissent = Refusal(verdict, GateThreeRules.Dissent);
        Assert.NotNull(dissent);
        Assert.Contains("verifier 2", dissent!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("verifier 1", dissent.Message, StringComparison.Ordinal);
        Assert.False(verdict.Accepted);
    }

    /// <summary>The first verifier dissenting is named as the first verifier, not as "a verifier".</summary>
    [Fact]
    public void The_first_verifier_dissenting_is_named_as_the_first()
    {
        var verdict = Review(
            one: Solve(VerifierA, 0.05m, 0.90m, 0.03m, 0.02m),
            two: Agrees(VerifierB));

        var dissent = Refusal(verdict, GateThreeRules.Dissent);
        Assert.NotNull(dissent);
        Assert.Contains("verifier 1", dissent!.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("verifier 2", dissent.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// When both dissent, both are named. Reporting one and stopping is the shape that makes an author
    /// re-run the model that was fine.
    /// </summary>
    [Fact]
    public void Both_verifiers_dissenting_names_both()
    {
        var verdict = Review(
            one: Solve(VerifierA, 0.05m, 0.90m, 0.03m, 0.02m),
            two: Solve(VerifierB, 0.02m, 0.03m, 0.90m, 0.05m));

        var named = verdict.Refusals
            .Where(r => r.Rule == GateThreeRules.Dissent)
            .Select(r => r.Message)
            .ToList();

        Assert.Equal(2, named.Count);
        Assert.Contains(named, m => m.Contains("verifier 1", StringComparison.Ordinal));
        Assert.Contains(named, m => m.Contains("verifier 2", StringComparison.Ordinal));
    }

    /// <summary>
    /// A verifier that puts 0.90 on the key and 0.60 on a distractor has not created a probability
    /// distribution; the weights are normalised first, and the gate still refuses for the distractor.
    /// This catches a `Normalise` that divides by a hard-coded 1 instead of the actual sum.
    /// </summary>
    [Fact]
    public void Weights_that_do_not_sum_to_one_are_normalised_before_the_thresholds()
    {
        // Sum is 2.0: A/B/C/D = 0.45/0.45/0.05/0.05 after normalising, so A is not at 0.85.
        var verdict = Review(one: Solve(VerifierA, 0.90m, 0.90m, 0.10m, 0.10m));

        Assert.Contains(verdict.Refusals, r => r.Rule == GateThreeRules.KeyConfidence);
    }

    // -----------------------------------------------------------------------------------------
    // the three families are distinct
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> the whole point of gate three. Two
    /// verifiers backed by one model produce two confident, agreeing distributions - the item looks
    /// perfectly verified and nothing was verified twice. The refusal must be about the families and
    /// must not also recite the thresholds, which would bury the one fact to act on.
    /// </summary>
    [Fact]
    public void Two_verifiers_from_one_family_are_refused_even_when_they_agree()
    {
        var verdict = Review(
            one: Agrees("gpt"),
            two: Agrees("gpt"));

        var refusal = Refusal(verdict, GateThreeRules.DistinctFamilies);
        Assert.NotNull(refusal);
        Assert.Equal(GateThreeRules.DistinctFamilies, Assert.Single(verdict.Refusals).Rule);
        Assert.Contains("verifier 1", refusal!.Message, StringComparison.Ordinal);
        Assert.Contains("verifier 2", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The author is never a verifier - checked on verifier one. This is the direction a guard most
    /// easily gets wrong, because "the verifiers must differ from each other" reads as sufficient
    /// until the author shares a family with one of them.
    /// </summary>
    [Fact]
    public void The_author_may_not_be_the_first_verifier()
    {
        var verdict = Review(one: Agrees("claude"), two: Agrees(VerifierB));

        var refusal = Refusal(verdict, GateThreeRules.DistinctFamilies);
        Assert.NotNull(refusal);
        Assert.Contains("verifier 1", refusal!.Message, StringComparison.Ordinal);
        Assert.Contains("authored", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// And on verifier two. A check that compares the author to the first verifier only passes the
    /// test above and fails this one, which is exactly the difference worth a second case.
    /// </summary>
    [Fact]
    public void The_author_may_not_be_the_second_verifier()
    {
        var verdict = Review(one: Agrees(VerifierA), two: Agrees("claude"));

        var refusal = Refusal(verdict, GateThreeRules.DistinctFamilies);
        Assert.NotNull(refusal);
        Assert.Contains("verifier 2", refusal!.Message, StringComparison.Ordinal);
        Assert.Contains("authored", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Three-way distinctness, not two-way: every pair is compared. Here the author differs from
    /// verifier one and verifier two differs from verifier one, but the author and verifier two are
    /// the same - a check written as "author != one &amp;&amp; one != two" accepts it.
    /// </summary>
    [Fact]
    public void All_three_pairs_are_compared()
    {
        var verdict = GateThree.Review(
            "gemini",
            "A",
            Agrees("gpt"),
            Agrees("gemini"),
            NoIdea("gemini"));

        Assert.Contains(verdict.Refusals, r => r.Rule == GateThreeRules.DistinctFamilies);
    }

    /// <summary>Family names are compared case- and whitespace-insensitively: "GPT " is "gpt".</summary>
    [Fact]
    public void Family_names_are_compared_without_case_or_padding()
    {
        var verdict = Review(one: Agrees(" GPT "), two: Agrees("gpt"));

        Assert.Contains(verdict.Refusals, r => r.Rule == GateThreeRules.DistinctFamilies);
    }

    /// <summary>
    /// An unnamed family cannot be <em>shown</em> distinct. Silence is not evidence of heterogeneity:
    /// a verifier with no declared family is indistinguishable from the author.
    /// </summary>
    [Fact]
    public void A_verifier_that_declares_no_family_is_refused()
    {
        var verdict = Review(two: Agrees("   "));

        var refusal = Refusal(verdict, GateThreeRules.DistinctFamilies);
        Assert.NotNull(refusal);
        Assert.Contains("verifier 2", refusal!.Message, StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------------------------
    // the stimulus-blind ablation
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// <b>Which guard could this pass while being wrong:</b> an ablation implemented as a fourth code
    /// path, or one compared against the most likely option instead of the key. 0.50 exactly is the
    /// boundary D17 says rejects ("at ≥ 0.50"), so a check written <c>&gt; 0.50</c> ships a
    /// world-knowledge item.
    /// </summary>
    [Fact]
    public void A_blind_solve_finding_the_key_at_exactly_the_ceiling_is_refused()
    {
        var verdict = Review(blind: Solve(VerifierB, 0.50m, 0.30m, 0.10m, 0.10m));

        var refusal = Refusal(verdict, GateThreeRules.WorldKnowledge);
        Assert.NotNull(refusal);
        Assert.Equal(GateThreeRules.WorldKnowledge, Assert.Single(verdict.Refusals).Rule);
        Assert.Contains("world knowledge", refusal!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("50", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>Just under the ablation ceiling is legal, so the rule is a ceiling and not a ban.</summary>
    [Fact]
    public void A_blind_solve_just_under_the_ceiling_is_accepted()
    {
        var verdict = Review(blind: Solve(VerifierB, 0.499m, 0.30m, 0.101m, 0.10m));

        Assert.True(verdict.Accepted, Rules(verdict));
    }

    /// <summary>
    /// The ablation reads the <em>key's</em> weight, not the solver's top choice. Here the solver's
    /// top choice is A (0.55) and the key B is only 0.20 - nothing about B was found blind, so the
    /// item passes even though the solver was confident about something. A rule that read the maximum
    /// would refuse a legal item.
    /// </summary>
    [Fact]
    public void The_ablation_reads_the_keys_weight_and_not_the_top_choice()
    {
        var verdict = Review(
            one: AgreesOn(VerifierA, "B"),
            two: AgreesOn(VerifierB, "B"),
            blind: Solve(VerifierB, 0.55m, 0.20m, 0.15m, 0.10m),
            key: "B");

        Assert.DoesNotContain(verdict.Refusals, r => r.Rule == GateThreeRules.WorldKnowledge);
    }

    // -----------------------------------------------------------------------------------------
    // an answer nobody can read is not an agreement
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// A verifier that returned nothing has verified nothing. <b>Which guard could this pass while
    /// being wrong:</b> a parser that defaults a missing label to 0 and then evaluates "is every
    /// distractor below 0.15" - true, vacuously - and "is the key at 0.85" against the one number it
    /// did get. The empty distribution must be refused as unusable, not read as agreement.
    /// </summary>
    [Fact]
    public void A_verifier_that_returns_no_distribution_refuses_the_item()
    {
        var empty = new GateThreeSolve(
            VerifierA,
            "gpt-model",
            new Dictionary<string, decimal>(StringComparer.Ordinal));

        var verdict = Review(one: empty);

        var refusal = Refusal(verdict, GateThreeRules.UnusableDistribution);
        Assert.NotNull(refusal);
        Assert.Contains("verifier 1", refusal!.Message, StringComparison.Ordinal);
        Assert.False(verdict.Accepted);
    }

    /// <summary>
    /// An ablation that returned nothing leaves the world-knowledge question unanswered, and an item
    /// whose ablation did not run is not verified. The same rule code, and the message names the
    /// ablation rather than a verifier.
    /// </summary>
    [Fact]
    public void An_ablation_that_returns_no_distribution_refuses_the_item()
    {
        var empty = new GateThreeSolve(
            VerifierB,
            "gemini-model",
            new Dictionary<string, decimal>(StringComparer.Ordinal));

        var verdict = Review(blind: empty);

        var refusal = Refusal(verdict, GateThreeRules.UnusableDistribution);
        Assert.NotNull(refusal);
        Assert.Contains("blind", refusal!.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A negative weight is not a probability. Refused as unusable rather than normalised into a
    /// smaller positive one, because a model that emitted -0.2 has not given a distribution.
    /// </summary>
    [Fact]
    public void A_negative_weight_is_not_a_distribution()
    {
        var verdict = Review(one: Solve(VerifierA, -0.2m, 0.4m, 0.4m, 0.4m));

        Assert.Contains(verdict.Refusals, r => r.Rule == GateThreeRules.UnusableDistribution);
    }

    /// <summary>
    /// A key the gate cannot name is refused rather than defaulted. Reading a null key as "A" would
    /// verify every productive task against option A.
    /// </summary>
    [Fact]
    public void A_key_outside_A_to_D_is_refused()
    {
        var verdict = Review(key: "");

        Assert.Contains(verdict.Refusals, r => r.Rule == GateThreeRules.UnusableDistribution);
    }

    /// <summary>
    /// The thresholds the gate enforces are the ones D17 names. A constant edited to 0.80 would make
    /// every test above pass with values shifted accordingly, so the specification's numbers are
    /// asserted directly.
    /// </summary>
    [Fact]
    public void The_thresholds_are_the_ones_the_specification_names()
    {
        Assert.Equal(0.85m, GateThree.RequiredKeyConfidence);
        Assert.Equal(0.15m, GateThree.DistractorCeiling);
        Assert.Equal(0.50m, GateThree.AblationCeiling);
    }
}
