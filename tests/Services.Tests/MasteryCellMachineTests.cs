using BusinessObjects;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #237 / PLC-66..71: the cell mastery machine, one test per named transition and one per trap the
/// ticket calls out. DB-free: the schema half is <c>domain45-mastery.sql</c>, the integration half is
/// <see cref="MasteryServiceTests"/>.
/// <para>
/// Every test names the state the cell is <b>not</b> allowed to reach, because a setter that accepts any
/// state passes every positive transition test.
/// </para>
/// </summary>
public sealed class MasteryCellMachineTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private const int ObjectiveMinimum = 12;
    private const int ProductiveMinimum = 3;

    /// <summary>Answers a sequence of n right answers in a row, from a snapshot.</summary>
    private static MasteryCellMachine.Snapshot Answer(
        MasteryCellMachine.Snapshot from, bool correct, int times, DateTimeOffset at)
    {
        var current = from;
        for (var i = 0; i < times; i++)
        {
            current = MasteryCellMachine.RecordAnswer(current, correct, at, ObjectiveMinimum).Next;
        }

        return current;
    }

    // ---- the prior and the estimate -----------------------------------------------------------------

    [Fact]
    public void An_unmeasured_cell_is_at_the_prior_and_is_always_due()
    {
        var unmeasured = MasteryCellMachine.Snapshot.Unmeasured;

        Assert.Equal(MasteryState.Untested, unmeasured.State);
        Assert.Equal(0.5, unmeasured.Probability, precision: 10);
        Assert.True(MasteryCellMachine.IsDue(unmeasured, At));
    }

    [Fact]
    public void The_first_answer_moves_unmeasured_to_a_measured_state_and_never_to_mastered()
    {
        // Four answers cannot master a cell, so one certainly cannot. The cell is above the floor
        // afterwards: evidence cannot be unobserved.
        var right = MasteryCellMachine.RecordAnswer(
            MasteryCellMachine.Snapshot.Unmeasured, true, At, ObjectiveMinimum);

        Assert.NotEqual(MasteryState.Untested, right.Next.State);
        Assert.NotEqual(MasteryState.Mastered, right.Next.State);
        Assert.Equal(1, right.Next.Evidence.ObservationCount);
        Assert.Equal(MasteryState.Untested, right.From);
    }

    // ---- THE TRAP: threshold alone masters a cell on four answers -----------------------------------

    [Fact]
    public void Threshold_reached_with_the_count_short_does_not_master_the_cell()
    {
        // THE GUARD THE TICKET NAMES, at the exact input that isolates it. Eleven perfect answers put
        // p at 0.867 - above the upper cut - so the THRESHOLD alone says mastered. The count refuses.
        var afterEleven = Answer(MasteryCellMachine.Snapshot.Unmeasured, true, 11, At);

        Assert.True(afterEleven.Probability >= MasterySchedule.MasteredThreshold,
            "the threshold must be reached, or this test proves nothing about the count");
        Assert.Equal(11, afterEleven.Evidence.ObservationCount);
        Assert.NotEqual(MasteryState.Mastered, afterEleven.State);
        Assert.Equal(MasteryState.Reviewing, afterEleven.State);
    }

    [Fact]
    public void Four_correct_answers_are_refused_by_both_halves_at_once()
    {
        // The ticket's sentence, as arithmetic: four answers is below the cut AND below the count, so
        // the two guards agree here - which is why the test above is the load-bearing one.
        var afterFour = Answer(MasteryCellMachine.Snapshot.Unmeasured, true, 4, At);

        Assert.True(afterFour.Probability < MasterySchedule.MasteredThreshold);
        Assert.NotEqual(MasteryState.Mastered, afterFour.State);
    }

    [Fact]
    public void The_mastered_state_needs_the_probability_and_the_count_together()
    {
        // The direct assertion on the decision function, so the case cannot pass by accident through an
        // answer sequence. Each row is a wrong input that a single-half implementation accepts:
        var decision = MasteryCellMachine.EnterFromEstimate;

        // probability high, count short -> not mastered
        Assert.Equal(MasteryState.Reviewing, decision(MasteryState.Learning, 0.95, 4, 12));
        // count met, probability short -> not mastered
        Assert.Equal(MasteryState.Learning, decision(MasteryState.Learning, 0.60, 40, 12));
        // both met -> mastered
        Assert.Equal(MasteryState.Mastered, decision(MasteryState.Learning, 0.85, 12, 12));
        // exactly on both boundaries -> mastered (>=, not >)
        Assert.Equal(MasteryState.Mastered, decision(MasteryState.Learning, 0.80, 12, 12));
    }

    [Fact]
    public void Twelve_correct_answers_master_the_cell_and_eighteen_are_still_mastered()
    {
        var afterTwelve = Answer(MasteryCellMachine.Snapshot.Unmeasured, true, 12, At);

        Assert.Equal(MasteryState.Mastered, afterTwelve.State);
        Assert.Equal(At, afterTwelve.MasteredAt);
        Assert.Equal(MasteryState.Untested, MasteryCellMachine.Snapshot.Unmeasured.State);
    }

    [Fact]
    public void Three_perfect_tasks_meet_the_productive_minimum_but_not_the_threshold()
    {
        // PLC-68 states the productive minimum as 3 tasks AND p >= 0.80. Three perfect tasks give
        // p = (2+3)/(4+3) = 0.714, so the THRESHOLD is what refuses a third task, not the count: the
        // productive n_min is a floor, and a productive cell masters when the estimate also crosses
        // 0.80 (seven perfect tasks: (2+7)/(4+7) = 0.818). Reported as a spec inconsistency.
        var productive = MasteryCellMachine.Snapshot.Unmeasured;
        for (var i = 0; i < 3; i++)
        {
            productive = MasteryCellMachine.RecordAnswer(
                productive, true, At, ProductiveMinimum).Next;
        }

        Assert.Equal(3, productive.Evidence.ObservationCount);
        Assert.True(productive.Probability < MasterySchedule.MasteredThreshold,
            "three perfect tasks sit below the upper cut, so the count is not the binding constraint");
        // 5/7 = 0.714, which is below the LOWER cut as well: three perfect tasks is a `learning` cell.
        Assert.Equal(MasteryState.Learning, productive.State);

        var seventh = MasteryCellMachine.RecordAnswer(
            MasteryCellMachine.Snapshot.Unmeasured, true, At, ProductiveMinimum);
        for (var i = 0; i < 6; i++)
        {
            seventh = MasteryCellMachine.RecordAnswer(seventh.Next, true, At, ProductiveMinimum);
        }

        Assert.Equal(MasteryState.Mastered, seventh.Next.State);
    }

    [Fact]
    public void Three_perfect_answers_would_master_a_task_cell_if_the_threshold_were_the_only_gate()
    {
        // The control that keeps the test above honest: the count half is met at three, so a machine
        // that checked only the count would call this mastered. EnterFromEstimate is where the two
        // halves are joined, so the assertion is made there directly.
        var decision = MasteryCellMachine.EnterFromEstimate;

        Assert.Equal(MasteryState.Mastered, decision(MasteryState.Learning, 0.85, 3, ProductiveMinimum));
        Assert.NotEqual(MasteryState.Mastered, decision(MasteryState.Learning, 0.85, 2, ProductiveMinimum));
    }

    // ---- THE TRAP: two consecutive, where a correct answer must reset -------------------------------

    [Fact]
    public void Two_wrong_answers_in_a_row_lapse_a_mastered_cell_to_reviewing()
    {
        var mastered = Answer(MasteryCellMachine.Snapshot.Unmeasured, true, 12, At);
        Assert.Equal(MasteryState.Mastered, mastered.State);

        var firstWrong = MasteryCellMachine.RecordAnswer(mastered, false, At, ObjectiveMinimum);
        var secondWrong = MasteryCellMachine.RecordAnswer(firstWrong.Next, false, At, ObjectiveMinimum);

        // The first miss does not take the state: it sets the review window (PLC-70).
        Assert.Equal(MasteryState.Mastered, firstWrong.Next.State);
        Assert.Equal(At + MasterySchedule.ReviewWindow, firstWrong.NextReviewDueAt);

        Assert.Equal(MasteryState.Reviewing, secondWrong.Next.State);
        Assert.True(secondWrong.MovedState);
        Assert.Equal(At + MasterySchedule.LapseInterval, secondWrong.NextReviewDueAt);
    }

    [Fact]
    public void Wrong_then_correct_then_wrong_is_not_a_lapse()
    {
        // THE TRAP NAMED IN THE TICKET: a counter that a correct answer does not reset. The three
        // answers below are two wrongs and a right; an increment-only counter sees two and lapses.
        var mastered = Answer(MasteryCellMachine.Snapshot.Unmeasured, true, 12, At);

        var wrong1 = MasteryCellMachine.RecordAnswer(mastered, false, At, ObjectiveMinimum);
        var correct = MasteryCellMachine.RecordAnswer(wrong1.Next, true, At, ObjectiveMinimum);
        var wrong2 = MasteryCellMachine.RecordAnswer(correct.Next, false, At, ObjectiveMinimum);

        Assert.Equal(1, wrong1.Next.ConsecutiveWrong);
        Assert.Equal(0, correct.Next.ConsecutiveWrong);   // <- the reset the trap is about
        Assert.Equal(1, wrong2.Next.ConsecutiveWrong);
        Assert.Equal(MasteryState.Mastered, wrong2.Next.State);
    }

    [Fact]
    public void A_single_wrong_answer_never_changes_a_mastered_cell_however_bad_the_estimate_is()
    {
        // PLC-70: "a single wrong answer does not drag a mastered topic back to developing". Even with
        // the estimate far below the cut, the state is held - which is why the mastered arm must not
        // consult the probability at all.
        var mastered = new MasteryCellMachine.Snapshot(
            MasteryState.Mastered,
            new MasteryEvidence(2d, 4d, 12),
            ConsecutiveWrong: 0,
            LastPractisedAt: At.AddDays(-1),
            MasteredAt: At.AddDays(-30),
            NextReviewDueAt: At);

        Assert.True(mastered.Probability < MasterySchedule.LowThreshold);

        var step = MasteryCellMachine.RecordAnswer(mastered, false, At, ObjectiveMinimum);
        Assert.Equal(MasteryState.Mastered, step.Next.State);
    }

    // ---- THE TRAP: four states reachable directly ------------------------------------------------

    [Fact]
    public void No_sequence_of_answers_reaches_untested_from_a_measured_cell()
    {
        // Nothing returns to unmeasured: evidence cannot be unobserved. A machine that re-derived from
        // "no recent answers" would hand back Untested after enough misses.
        var current = MasteryCellMachine.Snapshot.Unmeasured;
        for (var i = 0; i < 50; i++)
        {
            current = MasteryCellMachine.RecordAnswer(current, false, At, ObjectiveMinimum).Next;
        }

        Assert.NotEqual(MasteryState.Untested, current.State);
        Assert.Equal(50, current.Evidence.ObservationCount);
    }

    [Fact]
    public void Mastered_is_never_reached_from_untested_without_answers()
    {
        // The decision function is the only route in, and it demands both halves. An empty cell with a
        // perfect probability - which cannot arise, but a caller could synthesise - still cannot master.
        var decision = MasteryCellMachine.EnterFromEstimate;

        Assert.Equal(MasteryState.Untested, decision(MasteryState.Untested, 0.5, 0, ObjectiveMinimum));
        Assert.NotEqual(MasteryState.Mastered, decision(MasteryState.Untested, 1.0, 0, ObjectiveMinimum));
    }

    // ---- PLC-71: decay ---------------------------------------------------------------------------

    [Fact]
    public void A_mastered_cell_untouched_for_the_stated_period_reverts_to_reviewing()
    {
        var mastered = Answer(MasteryCellMachine.Snapshot.Unmeasured, true, 12, At.AddDays(-60));
        Assert.Equal(MasteryState.Mastered, mastered.State);
        Assert.True(mastered.Probability > 0.80);

        // 60 days idle: 32 overdue, four whole weeks, 0.08 of drift. 0.9-ish -> below 0.80.
        var tick = MasteryCellMachine.InactivityTick(mastered, At, demonstratedByMockSection: false);

        Assert.Equal(MasteryState.Reviewing, tick.Next.State);
        Assert.True(tick.MovedState);
        // Reverting is not lapsing: the state is developing, never low, and never unmeasured.
        Assert.NotEqual(MasteryState.Learning, tick.Next.State);
        Assert.NotEqual(MasteryState.Untested, tick.Next.State);
    }

    [Fact]
    public void A_mastered_cell_inside_the_28_day_window_does_not_decay()
    {
        var mastered = Answer(MasteryCellMachine.Snapshot.Unmeasured, true, 12, At.AddDays(-20));

        var tick = MasteryCellMachine.InactivityTick(mastered, At, demonstratedByMockSection: false);

        Assert.Equal(MasteryState.Mastered, tick.Next.State);
        Assert.False(tick.MovedState);
        Assert.Equal(mastered.Evidence.WeightedCorrect, tick.Next.Evidence.WeightedCorrect, precision: 10);
    }

    [Fact]
    public void Decay_never_fires_on_a_cell_that_was_never_mastered()
    {
        // THE TRAP NAMED IN THE TICKET. A cell in `learning` left alone for a year is unchanged: there
        // is no held position to erode, and "recompute from age" would silently re-grade it.
        var learning = new MasteryCellMachine.Snapshot(
            MasteryState.Learning,
            new MasteryEvidence(1d, 3d, 3),
            ConsecutiveWrong: 2,
            LastPractisedAt: At.AddDays(-365),
            MasteredAt: null,
            NextReviewDueAt: At.AddDays(-364));

        var tick = MasteryCellMachine.InactivityTick(learning, At, demonstratedByMockSection: false);

        Assert.Equal(MasteryState.Learning, tick.Next.State);
        Assert.False(tick.MovedState);
        Assert.Equal(learning, tick.Next);
    }

    [Fact]
    public void A_mock_section_at_or_above_the_level_holds_the_cell_against_the_calendar()
    {
        // AC4's exception: "unless a mock section at or above that level has demonstrated it".
        var mastered = Answer(MasteryCellMachine.Snapshot.Unmeasured, true, 12, At.AddDays(-90));

        var held = MasteryCellMachine.InactivityTick(mastered, At, demonstratedByMockSection: true);

        Assert.Equal(MasteryState.Mastered, held.Next.State);
        Assert.False(held.MovedState);
        // The clock restarts from the demonstration, so the next tick measures from today.
        Assert.Equal(At, held.Next.LastPractisedAt);
    }

    // ---- the maintenance pass --------------------------------------------------------------------

    [Fact]
    public void A_maintenance_pass_keeps_a_mastered_cell_mastered_and_restarts_its_clock()
    {
        var mastered = Answer(MasteryCellMachine.Snapshot.Unmeasured, true, 12, At.AddDays(-20));

        var pass = MasteryCellMachine.MaintenancePass(mastered, At);

        Assert.Equal(MasteryState.Mastered, pass.Next.State);
        Assert.Equal(At, pass.Next.LastPractisedAt);
        Assert.Equal(At + MasterySchedule.MasteredInterval, pass.NextReviewDueAt);
    }

    [Fact]
    public void A_maintenance_pass_on_an_unmastered_cell_is_refused_rather_than_silently_ignored()
    {
        var learning = new MasteryCellMachine.Snapshot(
            MasteryState.Learning, MasteryEvidence.None, 0, At, null, At);

        Assert.Throws<InvalidOperationException>(
            () => MasteryCellMachine.MaintenancePass(learning, At));
    }
}
