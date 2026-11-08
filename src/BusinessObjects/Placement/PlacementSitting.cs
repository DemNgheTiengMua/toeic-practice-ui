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
/// The outer bound is the second half of PLC-13's expiry - <c>started_at + 72 h</c> - because a
/// half-sitting must not linger forever. The first half (<c>last_activity + 48 h</c>) needs a
/// last-activity stamp the attempt table does not carry yet; that window and its "shorter of the two"
/// rule are #216's, and this type deliberately does not invent a second one.
/// </para>
/// </summary>
public static class PlacementSitting
{
    /// <summary>PLC-13: the outer bound, <c>started_at + 72 h</c>.</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(72);

    /// <summary>
    /// Whether this purpose runs a countdown and a phase clock. Every paper does, except a placement.
    /// A purpose string is the column the attempt already carries, so no second flag has to be kept in
    /// step with it.
    /// </summary>
    public static bool HasCountdown(string? examPurpose) =>
        !string.Equals(examPurpose, ExamPurpose.Placement.ToCode(), StringComparison.OrdinalIgnoreCase);

    /// <summary>The deadline a placement attempt gets: the cap, never the exam's own duration.</summary>
    public static DateTimeOffset ExpiresAt(DateTimeOffset startedAt) => startedAt + MaxLifetime;
}
