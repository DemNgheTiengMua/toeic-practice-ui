namespace Contracts;

public sealed record AttemptSkillScoreDto(
    int SkillId,
    string SkillCode,
    string SkillName,
    ModalityDto Modality,
    int RawScore,
    int ScaledScore,
    int TotalQuestions);
