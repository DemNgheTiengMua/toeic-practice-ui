using Contracts;
namespace WebMVC.Models;
public sealed record SimulatorSkillPresentation(SimulatorSkillDto Skill)
{
    public int Headroom => Math.Max(0, Skill.MaxScaledScore - Skill.BaseScaledScore);
    public int MidTick => Skill.Step > 0 ? Skill.MinScaledScore + (((Skill.MaxScaledScore - Skill.MinScaledScore) / Skill.Step / 2) * Skill.Step)
        : (Skill.MinScaledScore + Skill.MaxScaledScore) / 2;
}
