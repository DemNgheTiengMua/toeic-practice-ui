namespace BusinessObjects;

public sealed record AttemptPhaseInfo(
    int SkillId,
    string SkillCode,
    string SkillName,
    int SkillDisplayOrder,
    int StartOffsetSeconds,
    int DurationSeconds);
