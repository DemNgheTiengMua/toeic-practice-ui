namespace BusinessObjects;

/// <summary>
/// #241 / LRN-14: the next action is chosen by a fixed cascade, and the cascade is <b>data</b>.
/// <para>
/// The order is the whole decision. Every step is evaluated in this sequence and the first whose
/// condition holds wins, so the same learner state always produces the same recommendation. "The
/// first thing found" is not that rule: it lets whichever list happens to be longer decide the
/// answer, which is reproducible only by accident.
/// </para>
/// <para>
/// <b>Why it is a list and not a chain of ifs.</b> A chain encodes order implicitly, in the order the
/// branches were typed, and no test can assert it without reading the source. Here
/// <see cref="Order"/> is the assertion, and <see cref="NextAction.CascadeStep"/> names the step that
/// fired so a test can prove the higher-priority condition existed and lost.
/// </para>
/// <para>
/// <b>Step 1 is named for what it does, not for one of its cases.</b> LRN-14 says "resume an
/// in-progress placement"; a mock or a skill-practice sitting is also in progress and outranks
/// everything below it for the same reason (the learner has already paid the time). The placement is
/// checked first <em>inside</em> the step, so the spec's case still wins when both exist.
/// </para>
/// </summary>
public static class NextActionCascade
{
    public const string ResumeInProgress = "resume_in_progress";
    public const string RetryUnmeasuredSkill = "retry_unmeasured_skill";
    public const string NextStep = "next_step";
    public const string FreeDrill = "free_drill";
    public const string DailyCeiling = "daily_ceiling";
    public const string Completion = "completion";

    /// <summary>The cascade, highest priority first. LRN-14's six steps, in its order.</summary>
    public static IReadOnlyList<string> Order { get; } =
        [ResumeInProgress, RetryUnmeasuredSkill, NextStep, FreeDrill, DailyCeiling, Completion];
}
