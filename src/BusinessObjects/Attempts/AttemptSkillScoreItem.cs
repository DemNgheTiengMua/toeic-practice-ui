namespace BusinessObjects;

public sealed record AttemptSkillScoreItem(
    int SkillId,
    string SkillCode,
    string SkillName,
    Modality Modality,
    int RawScore,
    int ScaledScore,
    int TotalQuestions);
