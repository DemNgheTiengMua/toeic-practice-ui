namespace BusinessObjects;

/// <summary>
/// #235 / PLC-61: a coordinate the path currently prioritises, highlighted on the free-drill screen so
/// a learner who has not planned anything is not left guessing. Seeded by the plan (topic weakness and
/// the week's own steps) — it is a <b>hint</b>, never a restriction.
/// </summary>
public sealed record PrioritisedCoordinate(
    int SkillId,
    string SkillCode,
    string SkillName,
    Modality Modality,
    int TopicId,
    string TopicCode,
    string TopicName,
    CefrLevel Level,
    string Reason);

/// <summary>#235 / PLC-62: a step that stands between the learner and free drilling, with the minutes it
/// still needs. The lock <b>names</b> these rather than saying "come back later".</summary>
public sealed record IncompleteStep(int StepId, int WeekNumber, string Title, string TopicCode, int EstimatedMinutes);

/// <summary>
/// #235 / PLC-61..64: what the free-drill screen needs to be honest.
/// <para>
/// <b>Unrestricted but recommended.</b> Any examined skill, topic and level may be drilled; the top three
/// coordinates the plan prioritises are highlighted. <b>Locked until the week's steps are done</b>, and
/// the locked state names the incomplete steps and the minutes they still need — a lock with no
/// explanation is the most hostile state in the product.
/// </para>
/// <para>
/// <b>The daily ceiling</b> is read from <c>vw_DailyPracticeVolume</c> and never stored: reaching it
/// locks further drills with a notice that overnight spacing is required, offering review or lessons
/// instead.
/// </para>
/// </summary>
public sealed record FreeDrillState(
    bool IsUnlocked,
    IReadOnlyList<IncompleteStep> IncompleteSteps,
    int RemainingMinutes,
    IReadOnlyList<PrioritisedCoordinate> Priorities,
    bool CellUsedToday,
    bool DailyCeilingReached,
    int AnsweredToday,
    int DailyItemCeiling,
    string? LockNotice)
{
    /// <summary>
    /// The day's ceiling on objective items. PLC-64 gives a <b>range</b>, "50–60 objective items or 4
    /// productive tasks overall", and no ticket narrows it. The ceiling is the top of that range, so a
    /// learner is never cut short inside the band the spec allows; the number lives here, once, because
    /// it is the only place it is compared against.
    /// <para>
    /// <b>This is a product number, not a content quantity.</b> AGENTS.md §9 gives volume figures to
    /// content data once the pool exists; when it does, this becomes a read and this constant goes.
    /// </para>
    /// </summary>
    public const int DailyObjectiveCeiling = 60;

    /// <summary>PLC-64's productive half: "4 productive tasks overall". Same shape as the item ceiling.</summary>
    public const int DailyProductiveCeiling = 4;

    public static FreeDrillState Unrestricted(
        IReadOnlyList<PrioritisedCoordinate> priorities, bool cellUsedToday, int answeredToday, bool ceilingReached) =>
        new(true, [], 0, priorities, cellUsedToday, ceilingReached, answeredToday, DailyObjectiveCeiling, null);
}
