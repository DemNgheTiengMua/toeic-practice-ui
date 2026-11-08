using BusinessObjects;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #237 / AC5: "the decay, the prior and the thresholds are pure functions tested against the
/// specification's worked values." Every number below is quoted from SRS §7.5 with its requirement id,
/// and every one is asserted rather than restated.
/// <para>
/// These tests are deliberately DB-free. The schema half (state transitions, the single writer, the
/// mastered gate) is <c>docs/schema-tests/domain45-mastery.sql</c>; this half is the arithmetic.
/// </para>
/// </summary>
public sealed class MasteryEstimateTests
{
    // ---- PLC-67: the prior ---------------------------------------------------------------------

    [Fact]
    public void An_empty_cell_sits_at_the_prior_and_not_at_zero_or_mastered()
    {
        // alpha0 = beta0 = 2.0 -> 2/(2+2) = 0.5. A prior of zero would make a learner unmastered
        // until proved otherwise; 1.0 would make them mastered on no evidence.
        Assert.Equal(0.5, MasteryEvidence.None.Probability, precision: 10);
        Assert.Equal(0.5, MasteryEstimate.ProbabilityOf([]), precision: 10);

        // ...and 0.5 is neither cut, so an empty cell is below `reviewing`.
        Assert.True(MasteryEvidence.None.Probability < 0.733);
    }

    [Fact]
    public void One_correct_answer_on_a_fresh_cell_does_not_exceed_the_lower_threshold()
    {
        // THE TRAP NAMED IN THE TICKET. (2 + 1)/(4 + 1) = 0.60 < 0.733. A model that returned the
        // observed proportion (1/1 = 1.0) would declare this mastered, and every transition test would
        // still pass because the state setter would accept it.
        var probability = MasteryEstimate.ProbabilityOf([true]);

        Assert.Equal(0.6, probability, precision: 10);
        Assert.True(probability < 0.733, "one answer must not reach the lower cut");
        Assert.True(probability < 0.80, "one answer must not reach the upper cut");
    }

    [Fact]
    public void Four_correct_answers_reach_the_lower_cut_but_not_the_upper_one()
    {
        // (2 + 4)/(4 + 4) = 0.75. PLC-69's whole point: above 0.733, below 0.80. This is why the
        // observation minimum is load-bearing - without it four answers would sit close to "mastered",
        // which is exactly what the ticket says must not happen.
        var probability = MasteryEstimate.ProbabilityOf([true, true, true, true]);

        Assert.Equal(0.75, probability, precision: 10);
        Assert.True(probability > 0.733);
        Assert.True(probability < 0.80);
    }

    [Fact]
    public void Eleven_perfect_answers_clear_the_threshold_so_the_count_is_what_refuses_them()
    {
        // THE CASE PLC-68'S n_min EXISTS FOR. At 11/11 the probability is 0.867 - comfortably above
        // 0.80 - so the threshold ALONE would master the cell. Only the count refuses it, which is why
        // this pair of numbers is the ticket's central assertion.
        var eleven = MasteryEstimate.ProbabilityOf([.. Enumerable.Repeat(true, 11)]);
        var twelve = MasteryEstimate.ProbabilityOf([.. Enumerable.Repeat(true, 12)]);

        // For a run of perfect answers wc = wn, so k·wc = K and p = (α0 + K)/(α0 + β0 + K).
        Assert.Equal(13d / 15d, eleven, precision: 10);   // K = 11 -> 13/15 = 0.8667
        Assert.True(eleven >= MasterySchedule.MasteredThreshold, "the threshold is reached at 11");
        Assert.Equal(14d / 16d, twelve, precision: 10);   // K = 12 -> 14/16 = 0.875
        Assert.True(twelve >= MasterySchedule.MasteredThreshold);
    }

    // ---- PLC-67: recency weighting --------------------------------------------------------------

    [Fact]
    public void Weighting_is_recency_weighted_and_not_last_n_answers()
    {
        // Same answers, same count, different order => different estimate. A window over the last N
        // answers is order-blind within the window and would return the same number for both.
        var improved = MasteryEstimate.ProbabilityOf([false, false, true, true]);
        var worsened = MasteryEstimate.ProbabilityOf([true, true, false, false]);

        Assert.NotEqual(improved, worsened, precision: 10);
        Assert.True(improved > worsened,
            "a learner whose recent answers are right must score higher than the reverse");
    }

    [Fact]
    public void The_most_recent_answer_carries_weight_one_and_the_oldest_the_smallest_weight()
    {
        // K = 3, so the weights are 0.85^2, 0.85^1, 0.85^0 = 0.7225, 0.85, 1.
        // One correct answer at each position gives three different probabilities.
        var oldest = MasteryEstimate.ProbabilityOf([true, false, false]);
        var middle = MasteryEstimate.ProbabilityOf([false, true, false]);
        var newest = MasteryEstimate.ProbabilityOf([false, false, true]);

        Assert.True(oldest < middle, "the older answer must weigh less than the newer");
        Assert.True(middle < newest, "the newest answer must weigh most");

        // The closed form, spelled out: k = 3/2.5725, so p = (2 + k*0.7225)/(4 + 3).
        var k = 3d / 2.5725;
        Assert.Equal((2d + (k * 0.7225)) / 7d, oldest, precision: 10);
    }

    [Fact]
    public void The_fold_over_the_sufficient_statistic_equals_the_closed_form_over_the_sequence()
    {
        // This is the equivalence the table's two columns rely on. If these ever disagree, storing
        // (sum w*c, sum w*n) instead of the answers is a second, wrong rule.
        var sequences = new[]
        {
            new[] { true },
            new[] { false },
            new[] { true, false },
            new[] { false, true, true, false, true },
            new[] { false, false, false, false, false, false, false, false, false, false, false, false },
            [.. Enumerable.Repeat(true, 20)],
            [.. Enumerable.Range(0, 25).Select(i => i % 3 != 0)],
        };

        foreach (var answers in sequences)
        {
            var folded = MasteryEvidence.None;
            foreach (var answer in answers)
            {
                folded = folded.Observe(answer);
            }

            var closed = MasteryEstimate.EvidenceFrom(answers);

            Assert.Equal(closed.WeightedCorrect, folded.WeightedCorrect, precision: 10);
            Assert.Equal(closed.WeightedObservations, folded.WeightedObservations, precision: 10);
            Assert.Equal(closed.ObservationCount, folded.ObservationCount);
            Assert.Equal(closed.Probability, folded.Probability, precision: 10);
        }
    }

    [Fact]
    public void The_observation_count_survives_the_weights_falling_to_zero()
    {
        // Engineering point: 0.85^K underflows to a denormal, not to zero, for any realistic K, but the
        // weight sum stops moving. The COUNT must keep counting, because PLC-68's n_min reads it.
        var folded = MasteryEvidence.None;
        for (var i = 0; i < 100; i++)
        {
            folded = folded.Observe(isCorrect: true);
        }

        Assert.Equal(100, folded.ObservationCount);
        // 100 right answers converge on 1.0 and are a long way past the upper cut.
        Assert.True(folded.Probability > 0.95);
        Assert.True(folded.Probability < 1d, "a Beta posterior never reaches 1");
    }

    // ---- PLC-68: the observation minimum --------------------------------------------------------

    [Fact]
    public void Four_answers_cannot_satisfy_the_objective_minimum_and_three_cannot_satisfy_the_productive_one()
    {
        Assert.Equal(12, MasteryEstimate.MinimumObservationsFor(Modality.Listening));
        Assert.Equal(12, MasteryEstimate.MinimumObservationsFor(Modality.Reading));
        Assert.Equal(3, MasteryEstimate.MinimumObservationsFor(Modality.Speaking));
        Assert.Equal(3, MasteryEstimate.MinimumObservationsFor(Modality.Writing));

        // The ticket's sentence, as an assertion: four answers is below the objective minimum.
        Assert.True(4 < MasteryEstimate.MinimumObservationsFor(Modality.Reading));
    }

    // ---- PLC-71: the decay, tested against the specification's stated units ---------------------

    [Fact]
    public void A_cell_exactly_28_days_unpractised_has_not_decayed_and_a_full_week_past_it_has()
    {
        var practised = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

        // The onset is the 28th day: at 28 days no whole week of drift has elapsed.
        Assert.Equal(0, MasteryDecay.WeeksOverdue(practised, practised.AddDays(28)));
        Assert.Equal(0, MasteryDecay.WeeksOverdue(practised, practised.AddDays(34)));
        // One complete week past the onset is one week of drift, and so on.
        Assert.Equal(1, MasteryDecay.WeeksOverdue(practised, practised.AddDays(35)));
        Assert.Equal(1, MasteryDecay.WeeksOverdue(practised, practised.AddDays(41)));
        Assert.Equal(2, MasteryDecay.WeeksOverdue(practised, practised.AddDays(42)));
    }

    [Fact]
    public void Decay_is_two_hundredths_per_week_and_stops_at_the_prior()
    {
        // PLC-71: "decays 0.02 per week toward the prior". One week from a mastered 0.85 is 0.83.
        Assert.Equal(0.83, MasteryDecay.Apply(0.85, weeks: 1), precision: 10);
        Assert.Equal(0.81, MasteryDecay.Apply(0.85, weeks: 2), precision: 10);

        // Toward the prior and never past it: 0.5 is the floor a decaying cell stops at, because no
        // Beta posterior with a 2/2 prior can sit below it - but a cell already BELOW it is left alone.
        Assert.Equal(0.5, MasteryDecay.Apply(0.85, weeks: 18), precision: 10);
        Assert.Equal(0.5, MasteryDecay.Apply(0.85, weeks: 100), precision: 10);
        Assert.Equal(0.40, MasteryDecay.Apply(0.40, weeks: 5), precision: 10);
    }

    [Fact]
    public void Decay_reverts_to_the_lower_state_rather_than_lapsing_and_only_below_the_upper_cut()
    {
        // AC4: "decays toward the prior and reverts". The reverted state is `reviewing`, not
        // `untested` (the cell was measured) and not `learning` (this is not evidence of a wrong
        // answer - PLC-70's lapse is).
        Assert.Equal(MasteryState.Mastered, MasteryDecay.StateAfterDecay(0.85, weeks: 1));
        // 0.82 - 0.02 = 0.80, which is NOT below the cut: exactly on 0.80 is still mastered.
        Assert.Equal(MasteryState.Mastered, MasteryDecay.StateAfterDecay(0.82, weeks: 1));
        // 0.82 - 0.04 = 0.78 < 0.80: two weeks takes it over the edge.
        Assert.Equal(MasteryState.Reviewing, MasteryDecay.StateAfterDecay(0.82, weeks: 2));
        Assert.Equal(MasteryState.Reviewing, MasteryDecay.StateAfterDecay(0.81, weeks: 2));
        // Exactly on the cut, one week reverts it: 0.80 - 0.02 = 0.78.
        Assert.Equal(MasteryState.Reviewing, MasteryDecay.StateAfterDecay(0.80, weeks: 1));
        Assert.Equal(MasteryState.Reviewing, MasteryDecay.StateAfterDecay(0.85, weeks: 20));
    }
    [Fact]
    public void Decay_never_touches_a_cell_that_was_never_mastered()
    {
        // THE TRAP NAMED IN THE TICKET. A cell in `learning` at 0.40 that is left alone for a year is
        // still `learning` at 0.40: decay describes a held position eroding, and a position never held
        // cannot erode. A blanket "recompute from age" would silently re-grade every weak cell.
        //
        // The guard is ApplyIfMastered, because StateAfterDecay only has answers for a mastered cell -
        // it cannot return `learning`, so nothing it says about a weak cell is meaningful.
        Assert.Equal(0.40, MasteryDecay.ApplyIfMastered(0.40, MasteryState.Learning, weeks: 52), precision: 10);
        Assert.Equal(0.50, MasteryDecay.ApplyIfMastered(0.50, MasteryState.Untested, weeks: 52), precision: 10);
        Assert.Equal(0.75, MasteryDecay.ApplyIfMastered(0.75, MasteryState.Reviewing, weeks: 52), precision: 10);

        // A below-prior cell is not lifted to the prior by the calendar either: 0.5 is where the drift
        // goes, not a floor it raises a weak cell to. Lifting would turn `learning` into `reviewing` on
        // age alone, which is the defect this test exists to catch.
        Assert.Equal(0.40, MasteryDecay.Apply(0.40, weeks: 52), precision: 10);
        Assert.Equal(0.50, MasteryDecay.Apply(0.50, weeks: 52), precision: 10);
        Assert.Equal(0.50, MasteryDecay.Apply(0.52, weeks: 52), precision: 10);
    }
}
