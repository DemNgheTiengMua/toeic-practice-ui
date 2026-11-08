namespace Contracts;

/// <summary>#235 / PLC-61: a coordinate the path currently prioritises, highlighted on the free-drill
/// screen. A hint, never a restriction.</summary>
public sealed record PrioritisedCoordinateDto(
    int SkillId,
    string SkillCode,
    string SkillName,
    ModalityDto Modality,
    int TopicId,
    string TopicCode,
    string TopicName,
    string LevelCode,
    string Reason);

/// <summary>#235 / PLC-62: a step standing between the learner and free drilling, with the minutes it
/// still needs. The lock names these rather than saying "come back later".</summary>
public sealed record IncompleteStepDto(int StepId, int WeekNumber, string Title, string TopicCode, int EstimatedMinutes);

/// <summary>
/// #235 / PLC-61..64: what the free-drill screen needs to be honest — whether drilling is open, which
/// coordinates the plan prioritises, and which lock is holding it.
/// </summary>
public sealed record FreeDrillStateDto(
    bool IsUnlocked,
    IReadOnlyList<IncompleteStepDto> IncompleteSteps,
    int RemainingMinutes,
    IReadOnlyList<PrioritisedCoordinateDto> Priorities,
    bool CellUsedToday,
    bool DailyCeilingReached,
    int AnsweredToday,
    int DailyItemCeiling,
    string? LockNotice);
