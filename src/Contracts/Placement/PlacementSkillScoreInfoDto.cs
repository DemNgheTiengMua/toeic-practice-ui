namespace Contracts;

public sealed record PlacementSkillScoreInfoDto(
    int SkillId,
    string SkillCode,
    string SkillName,
    int RawScore,
    int TotalQuestions,
    int ScaledScore,
    string? BandCode);
