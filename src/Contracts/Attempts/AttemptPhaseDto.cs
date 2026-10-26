namespace Contracts;

public sealed record AttemptPhaseDto(
    int SkillId,
    string SkillCode,
    string SkillName,
    int SkillDisplayOrder,
    int StartOffsetSeconds,
    int DurationSeconds);
