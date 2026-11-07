using System;

namespace BusinessObjects;

/// <summary>
/// #213 / D0.10 / PLC-07: the retake cooldown, and the one condition that waives it.
/// <para>
/// <b>It is a cooldown, not a cap.</b> The rule reads the MOST RECENT finished sitting and nothing else.
/// There is no count of sittings anywhere, so a learner on their tenth placement is treated exactly like
/// a learner on their second. A guard written as "refuse if they have placed twice" reads like a cooldown
/// and is a lifetime cap; the difference is that <see cref="Until"/> has no memory beyond one row.
/// </para>
/// <para>
/// <b>The waiver is an internal failure, specifically.</b> Only <c>ai_grading_failed</c> means the system
/// could not produce a result. <c>candidate_skipped</c> is the learner declining, and it does NOT waive.
/// <c>inactivity_timeout</c> does NOT waive either: from the stored row a timer that fired during an
/// outage is indistinguishable from a learner who walked away, and waiving on it hands back exactly the
/// reroll PLC-07 exists to prevent. D0.10's own wording — "zero if every measured skill failed to grade"
/// — names grading and nothing else.
/// </para>
/// </summary>
public static class PlacementCooldown
{
    /// <summary>D0.10: 24 hours after a finished sitting, with no lifetime cap.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>
    /// When the cooldown ends, or null when there is none. Null also means "allowed now", so the caller
    /// has one thing to check rather than two.
    /// </summary>
    /// <param name="previousTakenAt">When the most recent finished sitting finished; null if none.</param>
    /// <param name="previousFailedOnTheSystemSide">
    /// True only when that sitting measured nothing because every skill's grading failed. The caller
    /// decides this from the skill rows; a skipped skill is not a system failure.
    /// </param>
    /// <param name="now">The clock, passed in so the rule is testable without waiting a day.</param>
    public static DateTimeOffset? Until(
        DateTimeOffset? previousTakenAt,
        bool previousFailedOnTheSystemSide,
        DateTimeOffset now)
    {
        if (previousTakenAt is not DateTimeOffset taken || previousFailedOnTheSystemSide)
        {
            return null;
        }

        var until = taken + Window;
        return until > now ? until : null;
    }
}
