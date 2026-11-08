using System;

namespace BusinessObjects;

/// <summary>
/// #214 / PLC-12, PLC-13: <b>a placement sitting has no clock.</b> Placement measures ability, not
/// speed, so the sitting is "in progress" exactly while the server says so and no elapsed time is
/// deducted while the tab is closed.
/// <para>
/// <b>Why the exam's own duration is not the deadline.</b> <c>Exams.DurationMinutes</c> is a mock
/// exam's wall clock - it is what makes a 120-minute mock a mock. Reusing it for placement is what put
/// a countdown on the page and auto-submitted a learner who stepped away to answer the door, which is
/// the exact behaviour PLC-12 forbids. It also set a <i>per-skill</i> phase deadline, so the first
/// Listening budget expiring ended the whole sitting.
/// </para>
/// <para>
/// <b>The deadline is the shorter of two windows</b> (PLC-13, #216): <c>min(last_activity + 48 h,
/// started_at + 72 h)</c>. The outer bound stops a half-sitting lingering forever; the inactivity arm
/// lets a 60-minute four-skill sitting fit around a working day and then ends a sitting nobody came
/// back to. It is <i>stamped by a write and never by a read</i>: a reload that moved the clock would
/// make refreshing a way to keep a dead sitting alive (and, with the countdown, a free skip).
/// </para>
/// </summary>
public static class PlacementSitting
{
    /// <summary>PLC-13: the outer bound, <c>started_at + 72 h</c>.</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(72);

    /// <summary>PLC-13: the inactivity window, <c>last_activity + 48 h</c>.</summary>
    public static readonly TimeSpan Inactivity = TimeSpan.FromHours(48);

    /// <summary>
    /// Whether this purpose runs a countdown and a phase clock. Every paper does, except a placement.
    /// A purpose string is the column the attempt already carries, so no second flag has to be kept in
    /// step with it.
    /// </summary>
    public static bool HasCountdown(string? examPurpose) =>
        !string.Equals(examPurpose, ExamPurpose.Placement.ToCode(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The deadline a placement attempt carries: the shorter of the two windows PLC-13 names.
    /// <paramref name="lastActivityAt"/> is null until the sitting's first write, so a sitting that has
    /// not been touched since it opened is measured from its start.
    /// </summary>
    public static DateTimeOffset ExpiresAt(DateTimeOffset startedAt, DateTimeOffset? lastActivityAt = null) =>
        Min(startedAt + MaxLifetime, (lastActivityAt ?? startedAt) + Inactivity);

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
