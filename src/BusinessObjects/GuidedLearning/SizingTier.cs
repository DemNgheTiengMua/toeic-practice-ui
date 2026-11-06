namespace BusinessObjects;

/// <summary>
/// D10's sizing table, transcribed exactly. Three tiers and no others: a fourth would be a size nobody
/// measured the minutes for.
/// <para>
/// The tier follows the topic's mastery at the current level. A mastered topic's steps shorten; they are
/// never removed — that is <see cref="LearningPathPlanner.SizingTierFor"/>'s rule and the reason
/// <see cref="SizingTier.Mastered"/> still exists rather than the topic dropping out of the plan.
/// </para>
/// </summary>
public enum SizingTier
{
    /// <summary>5 items or 1 short task, ~6–8 minutes.</summary>
    Mastered,

    /// <summary>8–10 items, or 1 full task, or lesson + 4 items, ~12–15 minutes.</summary>
    Developing,

    /// <summary>Lesson (primary + remedial) + 12–15 items, or 2 tasks, ~20–25 minutes.</summary>
    Low,
}
