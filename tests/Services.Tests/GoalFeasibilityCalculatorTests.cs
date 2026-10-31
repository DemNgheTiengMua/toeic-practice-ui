using BusinessObjects;

namespace Services.Tests;

/// <summary>
/// #153: the feasibility formula, tested directly at the seam the handoff contract names
/// (<see cref="GoalFeasibilityCalculator"/>). No database, no clock — the calculator takes today
/// as an argument, so "B1 to B2 in 30 days at 6 h/week" is a fixed, checkable claim.
/// </summary>
public sealed class GoalFeasibilityCalculatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);

    // CumulativeStudyHours as seeded: B1 = 500, B2 = 800, C1 = 1150.
    private const int B1Hours = 500;
    private const int B2Hours = 800;
    private const int C1Hours = 1150;

    [Fact]
    public void Required_hours_are_the_difference_between_two_bands_floored_at_thirty()
    {
        var b1ToB2 = GoalFeasibilityCalculator.Evaluate(B1Hours, B2Hours, Today.AddDays(30), 6m, Today);
        var b1ToC1 = GoalFeasibilityCalculator.Evaluate(B1Hours, C1Hours, Today.AddDays(14), 6m, Today);
        var sameBand = GoalFeasibilityCalculator.Evaluate(B1Hours, B1Hours, Today, 1m, Today);

        Assert.Equal(300, b1ToB2!.RequiredHours);              // 800 - 500
        Assert.Equal(650, b1ToC1!.RequiredHours);              // 1150 - 500
        Assert.Equal(GoalFeasibilityCalculator.MinimumRequiredHours, sameBand!.RequiredHours);
    }

    [Fact]
    public void Weeks_are_whole_and_round_up_so_a_started_week_still_counts()
    {
        Assert.Equal(1, GoalFeasibilityCalculator.Evaluate(B1Hours, B2Hours, Today, 6m, Today)!.WeeksAvailable);
        Assert.Equal(2, GoalFeasibilityCalculator.Evaluate(B1Hours, B2Hours, Today.AddDays(8), 6m, Today)!.WeeksAvailable);
        Assert.Equal(5, GoalFeasibilityCalculator.Evaluate(B1Hours, B2Hours, Today.AddDays(30), 6m, Today)!.WeeksAvailable);
    }

    [Fact]
    public void A_goal_the_learner_has_the_hours_for_is_feasible()
    {
        // B1 to B2 (300 h) in 30 weeks at 10 h/week: 300 available, R = 1.00.
        var feasibility = GoalFeasibilityCalculator.Evaluate(B1Hours, B2Hours, Today.AddDays(210), 10m, Today);

        Assert.NotNull(feasibility);
        Assert.Equal(FeasibilityStatus.Feasible, feasibility.Status);
        Assert.Equal(300, feasibility.RequiredHours);
        Assert.Equal(30, feasibility.WeeksAvailable);
        Assert.Equal(300m, feasibility.AvailableHours);
        Assert.Equal(1.00m, feasibility.Ratio);
        Assert.Equal(10, feasibility.SuggestedWeeklyHours);
    }

    /// <summary>
    /// The ticket's headline example ("B1 to B2 in 30 days at 6 h/week → Feasible") does not survive
    /// its own formula: 300 required hours against 30 available is R = 0.10. The formula is the
    /// invariant; this test pins the arithmetic so nobody "fixes" the verdict by softening the rule.
    /// </summary>
    [Fact]
    public void B1_to_B2_in_thirty_days_at_six_hours_a_week_costs_sixty_hours_a_week()
    {
        var feasibility = GoalFeasibilityCalculator.Evaluate(B1Hours, B2Hours, Today.AddDays(30), 6m, Today);

        Assert.NotNull(feasibility);
        Assert.Equal(300, feasibility.RequiredHours);          // 800 - 500
        Assert.Equal(5, feasibility.WeeksAvailable);            // ceil(30 / 7)
        Assert.Equal(30m, feasibility.AvailableHours);          // 5 × 6
        Assert.Equal(0.10m, feasibility.Ratio);                 // 30 / 300
        Assert.Equal(60, feasibility.SuggestedWeeklyHours);     // ceil(300 / 5)
        Assert.Equal(FeasibilityStatus.Unrealistic, feasibility.Status);
    }

    [Fact]
    public void B1_to_C1_in_two_weeks_is_unrealistic_and_states_both_numbers()
    {
        var feasibility = GoalFeasibilityCalculator.Evaluate(B1Hours, C1Hours, Today.AddDays(14), 6m, Today);

        Assert.NotNull(feasibility);
        Assert.Equal(FeasibilityStatus.Unrealistic, feasibility.Status);
        Assert.Equal(650, feasibility.RequiredHours);           // 1150 - 500
        Assert.Equal(2, feasibility.WeeksAvailable);            // ceil(14 / 7)
        Assert.Equal(12m, feasibility.AvailableHours);          // 2 × 6
    }

    [Fact]
    public void A_ratio_above_the_stretch_floor_but_below_feasible_is_a_stretch_with_the_pace_that_fixes_it()
    {
        // 300 h required; 12 weeks × 13 h = 156 available; R = 0.52; fixing it needs ceil(300 / 12) = 25.
        var feasibility = GoalFeasibilityCalculator.Evaluate(B1Hours, B2Hours, Today.AddDays(84), 13m, Today);

        Assert.NotNull(feasibility);
        Assert.Equal(FeasibilityStatus.Stretch, feasibility.Status);
        Assert.Equal(0.52m, feasibility.Ratio);
        Assert.Equal(25, feasibility.SuggestedWeeklyHours);
    }

    [Fact]
    public void A_pace_the_plan_needs_above_the_ceiling_is_unrealistic_however_good_the_ratio_looks()
    {
        // 300 h in 10 weeks needs 30 h/week. R = 180/300 = 0.60 looks like a stretch, but the pace
        // the goal actually demands breaks the 25 h ceiling, so the verdict is Unrealistic.
        var feasibility = GoalFeasibilityCalculator.Evaluate(B1Hours, B2Hours, Today.AddDays(70), 18m, Today);

        Assert.NotNull(feasibility);
        Assert.Equal(0.60m, feasibility.Ratio);
        Assert.Equal(30, feasibility.SuggestedWeeklyHours);
        Assert.Equal(FeasibilityStatus.Unrealistic, feasibility.Status);
    }

    [Fact]
    public void A_declared_pace_above_the_ceiling_is_unrealistic_even_with_years_to_spare()
    {
        var feasibility = GoalFeasibilityCalculator.Evaluate(B1Hours, C1Hours, Today.AddYears(10), 26m, Today);

        Assert.NotNull(feasibility);
        Assert.Equal(FeasibilityStatus.Unrealistic, feasibility.Status);
    }

    [Fact]
    public void Bands_without_cumulative_hours_cannot_be_judged_and_answer_nothing()
    {
        // A certificate whose bands are CAT_B1 / SW_B2: no defensible hours figure exists.
        Assert.Null(GoalFeasibilityCalculator.Evaluate(null, B2Hours, Today.AddDays(30), 6m, Today));
        Assert.Null(GoalFeasibilityCalculator.Evaluate(B1Hours, null, Today.AddDays(30), 6m, Today));
        Assert.Null(GoalFeasibilityCalculator.Evaluate(null, null, Today.AddDays(30), 6m, Today));
    }

    [Fact]
    public void An_unrealistic_goal_reports_the_earliest_date_that_pace_would_reach()
    {
        var feasibility = GoalFeasibilityCalculator.Evaluate(B1Hours, C1Hours, Today.AddDays(14), 6m, Today);

        Assert.NotNull(feasibility);
        Assert.Equal(FeasibilityStatus.Unrealistic, feasibility.Status);
        // ceil(650 / 6) = 109 weeks from today — the "move the date back" escape hatch.
        Assert.Equal(Today.AddDays(109 * 7), feasibility.EarliestFeasibleDate);
    }
}
