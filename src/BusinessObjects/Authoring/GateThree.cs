using System;
using System.Collections.Generic;

namespace BusinessObjects;

/// <summary>
/// #196: gate three's rule names. A refusal carries one of these codes, so a caller can branch on the
/// rule without reading English, and the message says the same rule in words for the author.
/// </summary>
public static class GateThreeRules
{
    public const string DistinctFamilies = "gate3.families.distinct";
    public const string UnusableDistribution = "gate3.distribution.unusable";
    public const string KeyConfidence = "gate3.key.confidence";
    public const string DistractorCeiling = "gate3.distractor.ceiling";
    public const string Dissent = "gate3.verifier.dissent";
    public const string WorldKnowledge = "gate3.ablation.world_knowledge";
}

/// <summary>
/// #196: one option as a blind verifier sees it - a label and its text. There is deliberately no
/// <c>IsKey</c> field: the key is withheld by the shape of the request rather than by a promise in the
/// prompt, so a verifier cannot be handed the answer by a caller that forgot to strip it.
/// </summary>
public sealed record GateThreeOption(string Label, string Text);

/// <summary>
/// #196: what a blind verifier is asked. <see cref="Stimulus"/> is the passage or transcript and is
/// <c>null</c> for the stimulus-blind ablation - the ablation is the same solve with that one argument
/// removed, not a fourth kind of call. The key, the explanation and the per-option rationales are not
/// fields here, so "with the key and explanation withheld" is a property of the type.
/// </summary>
public sealed record GateThreeSolveRequest(
    string Stem,
    string? Stimulus,
    IReadOnlyList<GateThreeOption> Options)
{
    /// <summary>
    /// The same item with the passage or transcript withheld. Returns this request when it already
    /// carries no stimulus, so building an ablation twice is not an error.
    /// </summary>
    public GateThreeSolveRequest Blind() => Stimulus is null ? this : this with { Stimulus = null };
}

/// <summary>
/// #196: one model's solve - a distribution over the options, plus which model produced it so the
/// verdict can name a dissenter.
/// <para>
/// <b>What a probability here is.</b> No provider in this solution returns a calibrated posterior for
/// a chosen option. The number is a <em>self-reported distribution</em> the verifier is forced to emit
/// as JSON over the four labels. It is therefore a statement of the model's preference, not a
/// probability, and the 0.85 / 0.15 thresholds are consistency thresholds: they say the model is
/// lopsided towards one option, not that it is right 85% of the time. A deployment that can read
/// token log-probabilities should replace the parser and leave the thresholds alone.
/// </para>
/// </summary>
public sealed record GateThreeSolve(
    string ModelFamily,
    string ModelName,
    IReadOnlyDictionary<string, decimal> Probabilities);

/// <summary>#196: one rule broken, named by code and by sentence.</summary>
public sealed record GateThreeRefusal(string Rule, string Message);

/// <summary>#196: gate three's verdict on one item. A refusal blocks; there is no non-blocking notice.</summary>
public sealed record GateThreeVerdict(IReadOnlyList<GateThreeRefusal> Refusals)
{
    public bool Accepted => Refusals.Count == 0;
}

/// <summary>
/// #196: gate three - blind dual-model key verification and the stimulus ablation (CNT-37, D17).
/// A pure function: no database, no clock, no network. The models are already solved and passed in,
/// which is what makes the thresholds testable without spending money.
/// <para>
/// <b>Why two families.</b> A family that authored an item and then validates its key shares its own
/// blind spots (D17). The three checks below are separate on purpose, and each can fail on its own:
/// <list type="bullet">
/// <item>the families are distinct - author, verifier one, verifier two;</item>
/// <item>each verifier puts the key at or above 0.85 and every distractor below 0.15;</item>
/// <item>each verifier's most likely option is the key, which is the "disagrees" case and is named
/// separately from a weak preference;</item>
/// <item>a solver blind to the passage does not find the key at 0.50 or above, which would mean the
/// item measures world knowledge rather than comprehension.</item>
/// </list>
/// </para>
/// <para>
/// <b>What this gate cannot do.</b> It compares declared family names. Two deployments of the same
/// weights under different names, or one proxy that silently routes both to one model, pass this
/// check. Proving heterogeneity needs provider-side evidence the solution does not have; the check
/// is a declaration the operator is accountable for, and it is named as such rather than described
/// as a guarantee.
/// </para>
/// </summary>
public static class GateThree
{
    /// <summary>D17: the key must be selected at or above this probability.</summary>
    public const decimal RequiredKeyConfidence = 0.85m;

    /// <summary>D17: every distractor must sit strictly below this.</summary>
    public const decimal DistractorCeiling = 0.15m;

    /// <summary>D17: a blind solve finding the key at or above this rejects the item.</summary>
    public const decimal AblationCeiling = 0.50m;

    public const string AuthorName = "the author";
    public const string VerifierOneName = "verifier 1";
    public const string VerifierTwoName = "verifier 2";
    public const string AblationName = "the stimulus-blind solver";

    private static readonly string[] Labels = ["A", "B", "C", "D"];

    /// <summary>
    /// Judges one item. <paramref name="authorFamily"/> is the family that wrote the item;
    /// <paramref name="ablation"/> is a solve of the same item with the stimulus withheld.
    /// </summary>
    public static GateThreeVerdict Review(
        string authorFamily,
        string keyLabel,
        GateThreeSolve verifierOne,
        GateThreeSolve verifierTwo,
        GateThreeSolve ablation)
    {
        var refusals = new List<GateThreeRefusal>();

        // Homogeneity is checked first and alone: when the two solvers are one model, every threshold
        // below is one model agreeing with itself, and reporting four refusals for that would bury the
        // one fact the author has to act on.
        var homogeneous = CheckFamilies(authorFamily, verifierOne, verifierTwo, refusals);
        if (homogeneous is not null)
        {
            return new GateThreeVerdict(refusals);
        }

        var key = keyLabel?.Trim().ToUpperInvariant();
        if (key is not ("A" or "B" or "C" or "D"))
        {
            refusals.Add(new GateThreeRefusal(
                GateThreeRules.UnusableDistribution,
                $"the item's key is '{keyLabel}', which is not one of A-D, so no verifier's distribution can be read against it. Gate one refuses this item first; gate three cannot verify a key it cannot name."));
            return new GateThreeVerdict(refusals);
        }

        CheckVerifier(VerifierOneName, verifierOne, key!, refusals);
        CheckVerifier(VerifierTwoName, verifierTwo, key!, refusals);
        CheckAblation(ablation, key!, refusals);

        return new GateThreeVerdict(refusals);
    }

    // ----- the rules -------------------------------------------------------------------------

    /// <summary>
    /// Returns <c>null</c> when the three families are distinct, or the refusal it added. Names the
    /// pair that collided, because "the models must differ" leaves the author guessing which.
    /// </summary>
    private static GateThreeRefusal? CheckFamilies(
        string authorFamily,
        GateThreeSolve verifierOne,
        GateThreeSolve verifierTwo,
        List<GateThreeRefusal> refusals)
    {
        var author = Family(authorFamily);
        var first = Family(verifierOne.ModelFamily);
        var second = Family(verifierTwo.ModelFamily);

        if (author.Length == 0 || first.Length == 0 || second.Length == 0)
        {
            var missing = author.Length == 0 ? AuthorName
                : first.Length == 0 ? VerifierOneName
                : VerifierTwoName;
            refusals.Add(new GateThreeRefusal(
                GateThreeRules.DistinctFamilies,
                $"{missing} declares no model family, so the three families cannot be shown to be distinct. An unnamed verifier is indistinguishable from the author (CNT-37)."));
            return refusals[^1];
        }

        if (first == second)
        {
            refusals.Add(new GateThreeRefusal(
                GateThreeRules.DistinctFamilies,
                $"{VerifierOneName} and {VerifierTwoName} are both '{verifierOne.ModelFamily}', so their agreement is one model agreeing with itself rather than two independent solves (CNT-37)."));
            return refusals[^1];
        }

        if (author == first)
        {
            refusals.Add(new GateThreeRefusal(
                GateThreeRules.DistinctFamilies,
                $"{VerifierOneName} is '{verifierOne.ModelFamily}', the family that authored the item. Re-reading its own item shares its own blind spots, so it cannot verify its own key (CNT-37)."));
            return refusals[^1];
        }

        if (author == second)
        {
            refusals.Add(new GateThreeRefusal(
                GateThreeRules.DistinctFamilies,
                $"{VerifierTwoName} is '{verifierTwo.ModelFamily}', the family that authored the item. Re-reading its own item shares its own blind spots, so it cannot verify its own key (CNT-37)."));
            return refusals[^1];
        }

        return null;
    }

    /// <summary>
    /// The two per-verifier rules. A distribution that cannot be read is refused rather than skipped:
    /// a verifier that answered nothing has not verified anything.
    /// </summary>
    private static void CheckVerifier(
        string name,
        GateThreeSolve solve,
        string key,
        List<GateThreeRefusal> refusals)
    {
        var distribution = Normalise(solve.Probabilities);
        if (distribution is null)
        {
            refusals.Add(new GateThreeRefusal(
                GateThreeRules.UnusableDistribution,
                $"{name} ({solve.ModelFamily}/{solve.ModelName}) returned no usable distribution over A-D - {Describe(solve)} - so its solve cannot count as a verification (CNT-37)."));
            return;
        }

        var keyConfidence = distribution[key];
        if (keyConfidence < RequiredKeyConfidence)
        {
            refusals.Add(new GateThreeRefusal(
                GateThreeRules.KeyConfidence,
                $"{name} ({solve.ModelFamily}/{solve.ModelName}) put {Percent(keyConfidence)} on the key {key}, below the {Percent(RequiredKeyConfidence)} a blind solve must reach. A key two independent models only lean towards is a key nobody can defend (CNT-37)."));
        }

        foreach (var label in Labels)
        {
            if (label == key)
            {
                continue;
            }

            if (distribution[label] >= DistractorCeiling)
            {
                refusals.Add(new GateThreeRefusal(
                    GateThreeRules.DistractorCeiling,
                    $"{name} ({solve.ModelFamily}/{solve.ModelName}) put {Percent(distribution[label])} on distractor {label}, at or above the {Percent(DistractorCeiling)} ceiling. A solver splitting its weight between the key and one distractor is evidence the distractor is defensible, which makes the item unfair rather than hard (CNT-37)."));
                break;
            }
        }

        var picked = MostLikely(distribution);
        if (picked != key)
        {
            refusals.Add(new GateThreeRefusal(
                GateThreeRules.Dissent,
                $"{name} ({solve.ModelFamily}/{solve.ModelName}) dissents: it selects {picked} at {Percent(distribution[picked])}, not the key {key} at {Percent(keyConfidence)} (CNT-37)."));
        }
    }

    /// <summary>
    /// The ablation. Reads the same distribution shape as a verifier solve, so "without the passage"
    /// is one argument and not a second implementation of the reading.
    /// </summary>
    private static void CheckAblation(
        GateThreeSolve solve,
        string key,
        List<GateThreeRefusal> refusals)
    {
        var distribution = Normalise(solve.Probabilities);
        if (distribution is null)
        {
            refusals.Add(new GateThreeRefusal(
                GateThreeRules.UnusableDistribution,
                $"{AblationName} ({solve.ModelFamily}/{solve.ModelName}) returned no usable distribution over A-D - {Describe(solve)} - so whether the item can be answered without the passage is unknown, and an item whose world-knowledge check did not run is not verified (CNT-37)."));
            return;
        }

        var confidence = distribution[key];
        if (confidence >= AblationCeiling)
        {
            refusals.Add(new GateThreeRefusal(
                GateThreeRules.WorldKnowledge,
                $"{AblationName} ({solve.ModelFamily}/{solve.ModelName}) found the key {key} at {Percent(confidence)} with the passage or transcript withheld, at or above the {Percent(AblationCeiling)} ceiling. The item can be answered without reading the stimulus, so it measures world knowledge rather than comprehension (CNT-37)."));
        }
    }

    // ----- helpers ---------------------------------------------------------------------------

    /// <summary>
    /// A distribution over exactly A-D summing to 1, or <c>null</c>. The sum is divided out because a
    /// model may answer in percentages or in tenths; the four weights are relative, and normalising
    /// cannot turn a weak key into a strong one - it scales every label by the same factor.
    /// </summary>
    private static Dictionary<string, decimal>? Normalise(IReadOnlyDictionary<string, decimal>? probabilities)
    {
        if (probabilities is null)
        {
            return null;
        }

        var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var label in Labels)
        {
            var value = ProbabilityFor(probabilities, label);
            if (value < 0m)
            {
                return null;
            }

            values[label] = value;
        }

        var sum = 0m;
        foreach (var value in values.Values)
        {
            sum += value;
        }

        if (sum <= 0m)
        {
            return null;
        }

        foreach (var label in Labels)
        {
            values[label] = values[label] / sum;
        }

        return values;
    }

    private static decimal ProbabilityFor(IReadOnlyDictionary<string, decimal> probabilities, string label)
    {
        foreach (var pair in probabilities)
        {
            if (string.Equals(pair.Key?.Trim(), label, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return 0m;
    }

    /// <summary>The most likely label, first in A-D order on a tie. A tie with the key is not a dissent.</summary>
    private static string MostLikely(IReadOnlyDictionary<string, decimal> distribution)
    {
        var best = Labels[0];
        foreach (var label in Labels)
        {
            if (distribution[label] > distribution[best])
            {
                best = label;
            }
        }

        return best;
    }

    /// <summary>What the model actually returned, so a refusal about a bad distribution can be read.</summary>
    private static string Describe(GateThreeSolve solve)
    {
        if (solve.Probabilities is null || solve.Probabilities.Count == 0)
        {
            return "it returned nothing";
        }

        var parts = new List<string>();
        foreach (var pair in solve.Probabilities)
        {
            parts.Add($"{pair.Key}={pair.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
        }

        return string.Join(", ", parts);
    }

    private static string Percent(decimal value) =>
        (value * 100m).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";

    private static string Family(string? family) =>
        family?.Trim().ToLowerInvariant() ?? string.Empty;
}
