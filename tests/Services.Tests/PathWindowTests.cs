using System;
using System.Collections.Generic;
using System.Linq;
using BusinessObjects;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #233 / D10: the four-week window's arithmetic, as pure functions.
/// <para>
/// The trap this file exists to close: a calendar rule and an "all steps done" rule that compute
/// <b>different</b> boundaries close a week twice or never. There is one function,
/// <see cref="PathWindow.Closes"/>, and both conditions are its arguments.
/// </para>
/// </summary>
public sealed class PathWindowTests
{
    private static readonly DateOnly Day1 = new(2026, 1, 1);

    [Fact]
    public void The_window_is_four_weeks_of_seven_days()
    {
        Assert.Equal(4, PathWindow.Weeks);
        Assert.Equal(4, LearningPathPlanner.WindowWeeks);
        Assert.Equal(Day1.AddDays(6), PathWindow.WeekEndsOn(Day1));
    }

    [Fact]
    public void Nothing_closes_on_the_first_day_of_the_window()
    {
        Assert.Empty(PathWindow.Closes(Day1, 1, Day1, firstWeekAllDone: false));
    }

    [Fact]
    public void The_seventh_day_is_still_inside_the_first_week()
    {
        // "the calendar crosses day 8" - day 7 is inside week one.
        Assert.Empty(PathWindow.Closes(Day1, 1, Day1.AddDays(6), firstWeekAllDone: false));
    }

    [Fact]
    public void The_calendar_closes_week_one_on_day_eight()
    {
        var closed = PathWindow.Closes(Day1, 1, Day1.AddDays(7), firstWeekAllDone: false);

        var week = Assert.Single(closed);
        Assert.Equal(1, week.WeekNumber);
        Assert.Equal(Day1, week.StartedOn);
        Assert.Equal(Day1.AddDays(6), week.ClosedOn);
        Assert.Equal(PathWindow.CloseCalendar, week.CloseReason);
    }

    [Fact]
    public void A_learner_away_for_three_weeks_owes_three_closes()
    {
        var closed = PathWindow.Closes(Day1, 1, Day1.AddDays(21), firstWeekAllDone: false);

        Assert.Equal([1, 2, 3], closed.Select(c => c.WeekNumber));
        Assert.Equal(
            [Day1, Day1.AddDays(7), Day1.AddDays(14)],
            closed.Select(c => c.StartedOn));
        Assert.All(closed, c => Assert.Equal(PathWindow.CloseCalendar, c.CloseReason));
    }

    [Fact]
    public void A_finished_first_week_closes_early_and_says_so()
    {
        var closed = PathWindow.Closes(Day1, 1, Day1.AddDays(2), firstWeekAllDone: true);

        var week = Assert.Single(closed);
        Assert.Equal(PathWindow.CloseAllDone, week.CloseReason);
        Assert.Equal(Day1.AddDays(2), week.ClosedOn);
    }

    /// <summary>
    /// The input that would pass a two-function implementation while being wrong: three weeks have
    /// passed AND the first week is also finished. Two independent rules both say "close week one",
    /// and the naive sum closes four weeks — archiving a week that only just became live.
    /// </summary>
    [Fact]
    public void Both_conditions_together_close_three_weeks_and_not_four()
    {
        var closed = PathWindow.Closes(Day1, 1, Day1.AddDays(21), firstWeekAllDone: true);

        Assert.Equal(3, closed.Count);
        Assert.All(closed, c => Assert.Equal(PathWindow.CloseCalendar, c.CloseReason));
    }

    [Fact]
    public void A_clock_that_runs_backwards_closes_nothing()
    {
        // A device with a wrong date, or a restored catalogue, must not archive weeks nobody lived
        // through — and a negative count would move the window forwards.
        Assert.Empty(PathWindow.Closes(Day1, 1, Day1.AddDays(-30), firstWeekAllDone: false));
    }

    /// <summary>
    /// The next week's span after a slide must be measured from the window's <b>own</b> start, not
    /// from the path's creation date. A rule reading <c>StartedAt</c> would compute the same boundary
    /// the calendar already used and close the next week immediately.
    /// </summary>
    [Fact]
    public void A_calendar_close_consumes_the_seven_days_it_spent()
    {
        var closed = PathWindow.Closes(Day1, 1, Day1.AddDays(7), firstWeekAllDone: false).Single();
        var after = PathWindow.AfterClose(Day1, 1, closed);

        Assert.Equal(Day1.AddDays(7), after.WindowStart);
        Assert.Equal(2, after.ActiveWeek);

        // and on that same day, the new window owes nothing
        Assert.Empty(PathWindow.Closes(after.WindowStart, after.ActiveWeek, Day1.AddDays(7), firstWeekAllDone: false));
    }

    /// <summary>
    /// A work close happens early: week one finished on day two. The week that follows still runs to
    /// the original calendar boundary, so the window start does not move.
    /// </summary>
    [Fact]
    public void A_work_close_leaves_the_calendar_boundary_where_it_was()
    {
        var closed = PathWindow.Closes(Day1, 1, Day1.AddDays(2), firstWeekAllDone: true).Single();
        var after = PathWindow.AfterClose(Day1, 1, closed);

        Assert.Equal(Day1, after.WindowStart);
        Assert.Equal(2, after.ActiveWeek);

        // the window now owes nothing on day two, and one close on day eight
        Assert.Empty(PathWindow.Closes(after.WindowStart, after.ActiveWeek, Day1.AddDays(2), firstWeekAllDone: false));
        var onDayEight = PathWindow.Closes(after.WindowStart, after.ActiveWeek, Day1.AddDays(7), firstWeekAllDone: false);
        Assert.Equal(2, onDayEight.Single().WeekNumber);
    }

    [Fact]
    public void Live_weeks_are_the_active_one_and_the_three_after_it()
    {
        Assert.Equal(4, PathWindow.LastLiveWeek(1));
        Assert.Equal(9, PathWindow.LastLiveWeek(6));

        Assert.True(PathWindow.IsLive(1, 1));
        Assert.True(PathWindow.IsLive(4, 1));
        Assert.False(PathWindow.IsLive(5, 1));
        Assert.False(PathWindow.IsLive(0, 1));

        // a week the window has already passed is history
        Assert.False(PathWindow.IsLive(1, 2));
        Assert.True(PathWindow.IsLive(5, 2));
    }

    /// <summary>A step written by hand or by seed carries no week. It is not behind any window, so
    /// the window must not hide it, close it or refuse to write it.</summary>
    [Fact]
    public void A_step_with_no_week_is_live()
    {
        Assert.True(PathWindow.IsLive(null, 7));
    }

    [Fact]
    public void Only_unfinished_topics_are_requeued()
    {
        var requeue = PathWindow.TopicsToRequeue([
            (5, "done"),
            (9, "in_progress"),
            (9, "locked"),
            (3, "available"),
        ]);

        // a finished topic is measured, not unmastered; and 9 appears once however many steps named it
        Assert.Equal([3, 9], requeue);
    }
}
