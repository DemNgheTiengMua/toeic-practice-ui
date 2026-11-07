namespace BusinessObjects;

/// <summary>
/// #212 / PLC-19, PLC-33, PLC-35: which of five outcomes one skill had in a placement.
/// <para>
/// <b>This exists because a nullable level cannot answer the question.</b> Before #212 a null
/// <c>CefrLevel</c> meant "the learner skipped this skill", "the sitting timed out before it" and
/// "its free response is still in the AI queue" at the same time, and no screen could tell them apart.
/// A screen that guessed said "not measured" to a learner whose writing was simply still being graded.
/// </para>
/// <para>
/// <b>Two of the five are measurements.</b> <see cref="Measured"/> and
/// <see cref="EstimatedFromOneTask"/> count towards completion; <see cref="Grading"/>,
/// <see cref="NotMeasured"/> and <see cref="ExemptAccommodated"/> do not. The last is LRN-55: an
/// accommodated skill is excluded from the overall, so it is not evidence that anything was measured.
/// </para>
/// </summary>
public enum PlacementResultStatus
{
    /// <summary>A full measurement: objective items scored, or every planned productive task graded.</summary>
    Measured,

    /// <summary>One productive task graded and the other failed. PLC-35: a partial result beats none.</summary>
    EstimatedFromOneTask,

    /// <summary>A productive task is still in the AI queue. The placement is still in progress (PLC-16).</summary>
    Grading,

    /// <summary>Nothing measured this skill. Always carries a <see cref="PlacementFailureReason"/>.</summary>
    NotMeasured,

    /// <summary>Excluded by an accommodation (LRN-55). Carries no measurement and is not evidence.</summary>
    ExemptAccommodated,
}

/// <summary>
/// #212: the three reasons a skill ends up not measured. Closed, because a fourth value would be a
/// failure nobody designed, and a typo would read as "something went wrong".
/// </summary>
public static class PlacementFailureReason
{
    /// <summary>The learner chose not to attempt the skill.</summary>
    public const string CandidateSkipped = "candidate_skipped";

    /// <summary>The sitting timed out before the skill was reached or finished.</summary>
    public const string InactivityTimeout = "inactivity_timeout";

    /// <summary>Every planned productive task's AI grading failed, so there is no level to report.</summary>
    public const string AiGradingFailed = "ai_grading_failed";

    /// <summary>The three, for a CHECK-shaped validation before a write reaches the database.</summary>
    public static IReadOnlyList<string> All { get; } =
        [CandidateSkipped, InactivityTimeout, AiGradingFailed];
}