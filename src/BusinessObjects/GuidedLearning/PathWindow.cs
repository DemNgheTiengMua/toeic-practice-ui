using System;
using System.Collections.Generic;
using System.Linq;

namespace BusinessObjects;

/// <summary>
/// #233 / D10: the four-week window, as arithmetic.
/// <para>
/// <b>There is exactly one close rule.</b> The ticket's trap is a calendar rule and an "all steps done"
/// rule computing different boundaries, so a week closes twice or never. Both conditions are arguments
/// to <see cref="Closes"/>, and it returns the weeks to close — with their spans, their reasons and
/// their order — so nothing downstream has to re-derive a boundary from a second rule.
/// </para>
/// <para>
/// A slide closes the window's first week and appends one week at the far end, so the window is always
/// exactly <see cref="Weeks"/> weeks. Only the far end is new: the weeks in the middle keep their rows,
/// which is what makes rewriting history impossible rather than merely discouraged.
/// </para>
/// </summary>
public static class PathWindow
{
    /// <summary>D10: "exactly four weeks are live at any moment".</summary>
    public const int Weeks = 4;

    /// <summary>D10: "week 1 closes when all its steps are done <b>or the calendar crosses day 8</b>", so
    /// the last day inside a week is day 7 and a week is seven days long.</summary>
    public const int DaysPerWeek = 7;

    /// <summary>The two close conditions, as the codes the archive stores.</summary>
    public const string CloseAllDone = "all_done";

    public const string CloseCalendar = "calendar";

    /// <summary>One week the window owes a close for.</summary>
    /// <param name="WeekNumber">The week being archived, in the path's own numbering.</param>
    /// <param name="StartedOn">The day it began.</param>
    /// <param name="ClosedOn">The day it closed.</param>
    /// <param name="CloseReason">Which condition fired. Both close a week; only one says the learner
    /// finished the work, and the archive is honest about which.</param>
    public sealed record ClosedWeek(int WeekNumber, DateOnly StartedOn, DateOnly ClosedOn, string CloseReason);

    /// <summary>The last day inside the week that began on <paramref name="weekStart"/> — day 7.</summary>
    public static DateOnly WeekEndsOn(DateOnly weekStart) => weekStart.AddDays(DaysPerWeek - 1);

    /// <summary>
    /// D10: the weeks the window owes a close for, today, in order.
    /// <para>
    /// <paramref name="firstWeekAllDone"/> is "every step in the window's first week is done". It is
    /// passed in rather than looked up because this file is pure: the caller reads the steps, this
    /// decides the boundary, and neither can disagree about what a boundary is.
    /// </para>
    /// <para>
    /// <b>The calendar dominates when both fire.</b> If three weeks have passed and the first week is
    /// also finished, week one closed on its own last day — the same day the calendar says — and that
    /// is one close, not two. A second close for the same week would archive a week that only just
    /// became live, which is the "closes twice" failure the ticket names.
    /// </para>
    /// <para>
    /// A clock that runs backwards owes nothing: a device with a wrong date, or a restored catalogue,
    /// must not archive weeks nobody lived through.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ClosedWeek> Closes(
        DateOnly windowStart, int activeWeek, DateOnly today, bool firstWeekAllDone)
    {
        var firstWeek = FirstLiveWeek(activeWeek);
        var elapsed = Math.Max(0, (today.DayNumber - windowStart.DayNumber) / DaysPerWeek);

        if (elapsed > 0)
        {
            return Enumerable.Range(0, elapsed)
                .Select(i =>
                {
                    var started = windowStart.AddDays(i * DaysPerWeek);
                    return new ClosedWeek(firstWeek + i, started, WeekEndsOn(started), CloseCalendar);
                })
                .ToList();
        }

        // The calendar has not crossed yet, so the only thing that can close the first week is the
        // learner having finished it — and it closes early, on the day that happened.
        return firstWeekAllDone
            ? [new ClosedWeek(firstWeek, windowStart, today, CloseAllDone)]
            : [];
    }

    /// <summary>D10: how many weeks the window owes a close for. <see cref="Closes"/> is the rule;
    /// this is the count of what it returned, so the two can never disagree.</summary>
    public static int DueSlides(DateOnly windowStart, DateOnly today, bool firstWeekAllDone) =>
        Closes(windowStart, activeWeek: 1, today, firstWeekAllDone).Count;

    /// <summary>
    /// The window's position after one close. A calendar close consumes the seven days it spent; a
    /// work close does not, so the week that follows still ends on the same calendar boundary the
    /// spec's "day 8" names.
    /// </summary>
    public static (DateOnly WindowStart, int ActiveWeek) AfterClose(
        DateOnly windowStart, int activeWeek, ClosedWeek closed) =>
        closed.CloseReason == CloseCalendar
            ? (windowStart.AddDays(DaysPerWeek), activeWeek + 1)
            : (windowStart, activeWeek + 1);

    /// <summary>The first live week.</summary>
    public static int FirstLiveWeek(int activeWeek) => Math.Max(1, activeWeek);

    /// <summary>The last live week: the active week plus the three after it.</summary>
    public static int LastLiveWeek(int activeWeek) => FirstLiveWeek(activeWeek) + Weeks - 1;

    /// <summary>
    /// Whether a week is inside the live window. A step with <b>no</b> week (written by hand, or by a
    /// seed) belongs to no window slot and is therefore never hidden or closed by one.
    /// </summary>
    public static bool IsLive(int? weekNumber, int activeWeek) =>
        weekNumber is not { } week
        || (week >= FirstLiveWeek(activeWeek) && week <= LastLiveWeek(activeWeek));

    /// <summary>
    /// D10: the topics a closing week leaves unmastered, and therefore re-queues at the far end.
    /// <para>
    /// Only the week's <b>unfinished</b> steps are candidates. A step the learner has already closed
    /// has been measured, so its topic is not "unmastered" — it is answered, and re-queuing it would
    /// spend the new week's minutes on a topic the learner just finished.
    /// </para>
    /// </summary>
    public static IReadOnlyList<int> TopicsToRequeue(IEnumerable<(int TopicId, string Status)> closingWeekSteps) =>
        closingWeekSteps
            .Where(s => !string.Equals(s.Status, "done", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.TopicId)
            .Distinct()
            .OrderBy(t => t)
            .ToList();
}
