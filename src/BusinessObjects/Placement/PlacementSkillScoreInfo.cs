namespace BusinessObjects;

public sealed record PlacementSkillScoreInfo(
    int SkillId,
    string SkillCode,
    string SkillName,
    int RawScore,
    int TotalQuestions,
    int ScaledScore,
    string? BandCode);
