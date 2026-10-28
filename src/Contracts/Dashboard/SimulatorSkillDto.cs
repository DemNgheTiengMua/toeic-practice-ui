
namespace Contracts;

public sealed record SimulatorSkillDto(
    int SkillId,
    string SkillCode,
    string SkillName,
    string Modality,
    int BaseScaledScore,
    int MinScaledScore,
    int MaxScaledScore,
    int Step);
