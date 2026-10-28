
namespace BusinessObjects;

public sealed record SimulatorSkill(
    int SkillId,
    string SkillCode,
    string SkillName,
    string Modality,
    int BaseScaledScore,
    int MinScaledScore,
    int MaxScaledScore,
    int Step);
