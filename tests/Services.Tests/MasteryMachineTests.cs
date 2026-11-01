using BusinessObjects;

namespace Services.Tests;

/// <summary>#155: the mastery machine is a pure function, so every row of the transition table can be
/// checked directly, without a database and without a second of waiting. The integration tests in
/// <see cref="PracticeMasteryTests"/> prove the same rules through the real service and schema; these
/// name them one by one.</summary>
public sealed class MasteryMachineTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static MasteryMachine.Snapshot Untested => new(MasteryState.Untested, 0, 0, 0, 0, 0, null, null);

    private static MasteryMachine.Snapshot Learning(int answered = 1, int correct = 0) => new(
        MasteryState.Learning, answered, correct, 0, answered - correct, 1, At.AddHours(24), null);

    // ---- Untested ----

    [Fact]
    public void The_first_answer_moves_untested_to_learning_either_way()
    {
        var right = MasteryMachine.Apply(Untested, isCorrect: true, At, firstAnswerOfDay: true);
        Assert.Equal(MasteryState.Learning, right.Next.State);
        Assert.Equal(At.AddHours(24), right.NextReviewDueAt);

        var wrong = MasteryMachine.Apply(Untested, isCorrect: false, At, firstAnswerOfDay: true);
        Assert.Equal(MasteryState.Learning, wrong.Next.State);
        Assert.Equal(At.AddHours(24), wrong.NextReviewDueAt);
    }

    // ---- Learning ----

    [Fact]
    public void A_wrong_answer_in_learning_stays_in_learning_at_24_hours_and_resets_the_streak()
    {
        var step = MasteryMachine.Apply(Learning(answered: 4, correct: 3), isCorrect: false, At, firstAnswerOfDay: false);

        Assert.Equal(MasteryState.Learning, step.Next.State);
        Assert.Equal(0, step.Next.Streak);
        Assert.Equal(At.AddHours(24), step.NextReviewDueAt);
    }

    [Fact]
    public void Under_five_answers_stays_in_learning_even_at_a_hundred_percent()
    {
        // Four right answers is not evidence of five: accuracy alone does not qualify, and the streak
        // has not reached three either (it is one now).
        var step = MasteryMachine.Apply(Learning(answered: 3, correct: 3), isCorrect: true, At, firstAnswerOfDay: false);

        Assert.Equal(MasteryState.Learning, step.Next.State);
        Assert.Equal(At.AddHours(24), step.NextReviewDueAt);
    }

    [Fact]
    public void Seventy_five_percent_over_five_answers_moves_to_reviewing_at_three_days()
    {
        // 3 of 4 correct; the fifth answer makes 4 of 5 = 80% >= 75%.
        var step = MasteryMachine.Apply(Learning(answered: 4, correct: 3) with { Streak = 1 }, isCorrect: true, At, firstAnswerOfDay: false);

        Assert.Equal(MasteryState.Reviewing, step.Next.State);
        Assert.Equal(At.AddDays(3), step.NextReviewDueAt);
    }

    [Fact]
    public void A_run_of_three_correct_reaches_reviewing_below_the_accuracy_threshold()
    {
        // 2 of 6 correct = 33%: only the streak can carry this one, and it does at three.
        var before = new MasteryMachine.Snapshot(MasteryState.Learning, 6, 2, 2, 0, 1, At.AddHours(24), null);
        var step = MasteryMachine.Apply(before, isCorrect: true, At, firstAnswerOfDay: false);

        Assert.Equal(MasteryState.Reviewing, step.Next.State);
        Assert.Equal(At.AddDays(3), step.NextReviewDueAt);
    }

    // ---- Reviewing ----

    [Fact]
    public void A_reviewed_category_stretches_from_three_days_to_seven()
    {
        var due = At.AddDays(3);
        var reviewing = new MasteryMachine.Snapshot(MasteryState.Reviewing, 5, 4, 1, 0, 2, due, null);

        // Answered on the day it came due: the second pass stretches to a week.
        var step = MasteryMachine.Apply(reviewing, isCorrect: true, due, firstAnswerOfDay: true);

        Assert.Equal(MasteryState.Reviewing, step.Next.State);
        Assert.Equal(due.AddDays(7), step.NextReviewDueAt);
    }

    [Fact]
    public void A_wrong_answer_in_reviewing_drops_back_to_learning_at_24_hours()
    {
        var reviewing = new MasteryMachine.Snapshot(MasteryState.Reviewing, 8, 7, 3, 0, 2, At, null);
        var step = MasteryMachine.Apply(reviewing, isCorrect: false, At, firstAnswerOfDay: false);

        Assert.Equal(MasteryState.Learning, step.Next.State);
        Assert.Equal(At.AddHours(24), step.NextReviewDueAt);
    }

    // ---- Mastered ----

    [Fact]
    public void Ten_answers_across_two_days_at_eighty_five_percent_reaches_mastered_at_fourteen_days()
    {
        // 9 of 10 correct = 90%, two days: the proof the ticket asks for.
        var before = new MasteryMachine.Snapshot(MasteryState.Reviewing, 9, 8, 2, 0, 2, At, null);
        var step = MasteryMachine.Apply(before, isCorrect: true, At, firstAnswerOfDay: true);

        Assert.Equal(MasteryState.Mastered, step.Next.State);
        Assert.Equal(At.AddDays(14), step.NextReviewDueAt);
        Assert.Equal(At, step.Next.MasteredAt);
    }

    [Fact]
    public void Ten_answers_on_one_day_do_not_reach_mastered()
    {
        var before = new MasteryMachine.Snapshot(MasteryState.Reviewing, 9, 9, 9, 0, 1, At, null);
        var step = MasteryMachine.Apply(before, isCorrect: true, At, firstAnswerOfDay: false);

        Assert.Equal(MasteryState.Reviewing, step.Next.State);
        Assert.Null(step.Next.MasteredAt);
    }

    [Fact]
    public void Below_eighty_five_percent_never_reaches_mastered()
    {
        // 15 of 20 = 75%: good enough for Reviewing, not for Mastered, however many days it took.
        var before = new MasteryMachine.Snapshot(MasteryState.Reviewing, 19, 14, 1, 0, 3, At, null);
        var step = MasteryMachine.Apply(before, isCorrect: true, At, firstAnswerOfDay: false);

        Assert.NotEqual(MasteryState.Mastered, step.Next.State);
    }

    [Fact]
    public void A_single_wrong_answer_does_not_lapse_a_mastered_category_but_brings_it_back_tomorrow()
    {
        var mastered = new MasteryMachine.Snapshot(MasteryState.Mastered, 20, 18, 5, 0, 3, At, At.AddDays(-14));
        var step = MasteryMachine.Apply(mastered, isCorrect: false, At, firstAnswerOfDay: false);

        Assert.Equal(MasteryState.Mastered, step.Next.State);
        Assert.Equal(1, step.Next.ConsecutiveWrong);
        // Not another fortnight: a mastered category with a miss on it has to come round so the second
        // wrong answer can arrive, or the lapse rule is unreachable.
        Assert.Equal(At.AddHours(24), step.NextReviewDueAt);
    }

    [Fact]
    public void Two_wrong_answers_in_a_row_lapse_mastered_to_reviewing_at_24_hours()
    {
        var mastered = new MasteryMachine.Snapshot(MasteryState.Mastered, 20, 18, 0, 1, 3, At, At.AddDays(-14));
        var step = MasteryMachine.Apply(mastered, isCorrect: false, At, firstAnswerOfDay: false);

        Assert.Equal(MasteryState.Reviewing, step.Next.State);
        Assert.Equal(At.AddHours(24), step.NextReviewDueAt);
    }

    [Fact]
    public void A_lapse_keeps_the_date_mastered_was_earned()
    {
        var earned = At.AddDays(-30);
        var mastered = new MasteryMachine.Snapshot(MasteryState.Mastered, 20, 18, 0, 1, 3, At, earned);
        var step = MasteryMachine.Apply(mastered, isCorrect: false, At, firstAnswerOfDay: false);

        Assert.Equal(earned, step.Next.MasteredAt);
    }

    [Fact]
    public void A_right_answer_after_a_lapse_clears_the_wrong_run_but_not_the_state()
    {
        var lapsed = new MasteryMachine.Snapshot(MasteryState.Reviewing, 22, 18, 0, 1, 3, At, At.AddDays(-30));
        var step = MasteryMachine.Apply(lapsed, isCorrect: true, At, firstAnswerOfDay: false);

        Assert.Equal(0, step.Next.ConsecutiveWrong);
        Assert.Equal(MasteryState.Reviewing, step.Next.State);
    }

    // ---- Due-ness ----

    [Fact]
    public void An_untested_category_is_always_due_and_another_is_due_only_on_its_date()
    {
        Assert.True(MasteryMachine.IsDue(Untested, At));
        Assert.False(MasteryMachine.IsDue(
            new MasteryMachine.Snapshot(MasteryState.Learning, 1, 0, 0, 1, 1, At.AddHours(24), null), At));
        Assert.True(MasteryMachine.IsDue(
            new MasteryMachine.Snapshot(MasteryState.Learning, 1, 0, 0, 1, 1, At, null), At));
    }
}
