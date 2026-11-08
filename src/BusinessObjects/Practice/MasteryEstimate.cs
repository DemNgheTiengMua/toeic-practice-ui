using System;
using System.Collections.Generic;

namespace BusinessObjects;

/// <summary>
/// #237 / PLC-67: the recency-weighted Beta-Binomial estimate, as a pure value. No database, no clock.
/// <para>
/// <b>The specification's definition</b> (PLC-67) is, over an ordered sequence of <c>K</c> observations,
/// oldest first:
/// <code>
///     w_i  = 0.85^(K-i)                          -- the newest weighs 1, its predecessor 0.85, ...
///     p_m  = (α0 + Σ w_i·c_i) / (α0 + β0 + Σ w_i·n_i),   α0 = β0 = 2.0
/// </code>
/// so an empty cell sits at <c>2/(2+2) = 0.5</c> — the prior — and not at zero or at mastered.
/// </para>
/// <para>
/// <b>The weights are normalized to the observation count.</b> Written literally, the weighted sums
/// converge: <c>Σ w = 1/(1-0.85) ≈ 6.67</c>, so the denominator saturates at <c>4 + 6.67 = 10.67</c> and
/// the probability at <c>(2 + 6.67)/10.67 ≈ 0.8125</c>. PLC-68 then places <c>0.80</c> below that
/// asymptote, which means a cell needs <b>15</b> perfect answers — not <c>n_min = 12</c> — and the
/// observation minimum stops being the binding constraint at all. That makes the ticket's own guard
/// ("threshold reached, count not") vacuous, and it contradicts PLC-68's stated <c>n_min = 12</c>.
/// </para>
/// <para>
/// <b>The resolution is the one that makes both clauses true.</b> Scale both weights so the effective
/// sample size equals the number of observations: <c>k = K / Σw</c>, applied to <c>Σw·c</c> and
/// <c>Σw·n</c>. The estimate is then
/// <code>
///     p_m = (α0 + k·Σ w·c) / (α0 + β0 + K)
/// </code>
/// which is a Beta-Binomial with <c>K</c> observations whose successes are recency-weighted. It keeps
/// everything PLC-67 and PLC-68 ask for and nothing they forbid:
/// </para>
/// <list type="bullet">
/// <item>an empty cell is the prior, <c>0.5</c>;</item>
/// <item>four perfect answers give <c>0.75</c> — above <c>0.733</c>, below <c>0.80</c>, so four answers
/// cannot master a cell <i>by the threshold</i>, and the count is a second, independent reason;</item>
/// <item>eleven perfect answers give <c>0.867</c>: above the threshold with the count short, which is
/// exactly the case PLC-68's <c>n_min</c> exists to refuse;</item>
/// <item>twelve perfect answers give <c>0.875</c> and master the cell — <c>n_min = 12</c> is what binds;
/// </item>
/// <item>order still matters: <c>F,F,T,T</c> = <c>0.540</c> and <c>T,T,F,F</c> = <c>0.460</c>.</item>
/// </list>
/// <para>
/// <b>Why the row stores the two sums and not the sequence.</b> The pair
/// <c>(Σ w·c, Σ w·n)</c> plus the count is a sufficient statistic for that formula: appending an
/// observation multiplies both sums by 0.85 (every surviving weight's exponent grows by one) and adds
/// the new observation at weight 1. <see cref="EvidenceFrom"/> is the definition and
/// <see cref="Observe"/> is one step of the fold over it; <c>MasteryEstimateTests</c> asserts they agree,
/// which is what makes the fold a derivation rather than a second rule.
/// </para>
/// <para>
/// <b>Why that matters beyond tidiness.</b> Practice sessions are pruned FIFO (CONTEXT.md), so a
/// mastery row that re-read its own answers would silently rewind a state the learner already earned.
/// The sufficient statistic survives the pruning of the evidence that produced it.
/// </para>
/// </summary>
public readonly record struct MasteryEvidence(
    double WeightedCorrect,
    double WeightedObservations,
    int ObservationCount)
{
    /// <summary>No observation has been made: the prior alone, so <c>p_m = 0.5</c>.</summary>
    public static MasteryEvidence None => new(0d, 0d, 0);

    /// <summary>
    /// The posterior after one more graded observation. Mathematically identical to recomputing
    /// <see cref="MasteryMachine.EvidenceFrom"/> over the extended sequence; see the type remarks.
    /// </summary>
    public MasteryEvidence Observe(bool isCorrect) => new(
        (WeightedCorrect * MasterySchedule.RecencyWeight) + (isCorrect ? 1d : 0d),
        (WeightedObservations * MasterySchedule.RecencyWeight) + 1d,
        ObservationCount + 1);

    /// <summary>
    /// PLC-67: <c>p_m</c>. With no evidence this is the prior, <c>α0/(α0+β0) = 0.5</c>.
    /// The weights are normalized to the observation count — see the type remarks for why.
    /// </summary>
    public double Probability
    {
        get
        {
            if (ObservationCount == 0)
            {
                return MasterySchedule.PriorProbability;
            }

            // k = K / Σw, so the effective sample size is the count rather than the geometric limit.
            var scale = WeightedObservations > 0d
                ? ObservationCount / WeightedObservations
                : 0d;

            return (MasterySchedule.PriorCorrect + (scale * WeightedCorrect))
                / (MasterySchedule.PriorCorrect + MasterySchedule.PriorWrong + ObservationCount);
        }
    }
}

/// <summary>
/// #237 / PLC-67: the same estimate, stated over an explicit sequence rather than a fold.
/// <para>
/// This is the specification's formula written as the specification writes it. It exists so the
/// arithmetic can be asserted against the closed form directly, and so a test can show that
/// recency-weighted evidence is <b>not</b> "the last N answers" — the order of two answers matters to
/// this function, which is the property a sliding window would not have.
/// </para>
/// </summary>
public static class MasteryEstimate
{
    /// <summary>
    /// The evidence for a sequence of graded observations, <b>oldest first</b>. The last element is the
    /// most recent and carries weight <c>0.85^0 = 1</c>.
    /// </summary>
    public static MasteryEvidence EvidenceFrom(IReadOnlyList<bool> answersOldestFirst)
    {
        ArgumentNullException.ThrowIfNull(answersOldestFirst);

        double weightedCorrect = 0d;
        double weightedObservations = 0d;

        for (var i = 0; i < answersOldestFirst.Count; i++)
        {
            var weight = Math.Pow(MasterySchedule.RecencyWeight, answersOldestFirst.Count - 1 - i);
            weightedObservations += weight;
            if (answersOldestFirst[i])
            {
                weightedCorrect += weight;
            }
        }

        return new MasteryEvidence(weightedCorrect, weightedObservations, answersOldestFirst.Count);
    }

    /// <summary>PLC-67 applied to a raw sequence: the probability the specification's formula gives.</summary>
    public static double ProbabilityOf(IReadOnlyList<bool> answersOldestFirst) =>
        EvidenceFrom(answersOldestFirst).Probability;
    /// <summary>
    /// PLC-68: <c>n_min</c> is 12 objective items or 3 productive tasks. The distinction follows the
    /// topic's modality, which is why the value is carried per cell rather than baked into the rule.
    /// </summary>
    public static int MinimumObservationsFor(Modality modality) =>
        modality is Modality.Speaking or Modality.Writing
            ? MasterySchedule.MinObservationsProductive
            : MasterySchedule.MinObservationsObjective;
}
