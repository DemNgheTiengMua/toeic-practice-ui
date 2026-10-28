using Contracts;
using System.Text.Json;
namespace WebMVC.Models;

public sealed class DashboardSimulatorViewModel
{
    public SimulatorConfigDto Config { get; }
    public CertificateOptionDto Cert { get; }
    public int InitialTotal { get; }
    public ScoreBandThresholdDto InitialBand { get; }
    public string TargetCode { get; }
    public ScoreBandThresholdDto? TargetBandObj { get; }
    public int TargetMin { get; }
    public string TargetName { get; }
    public int InitialDeficit { get; }
    public bool HasExplicitTarget { get; }
    public SimulatorSkillDto TopLeverageSkill { get; }
    public int TopHeadroom { get; }
    public string BandsJson { get; }
    public IReadOnlyDictionary<int, SimulatorSkillPresentation> Skills { get; }
    public IReadOnlyDictionary<string, SimulatorBandPresentation> Bands { get; }
    public DashboardSimulatorViewModel(LearnerDashboardDto dashboard, SimulatorConfigDto sim)
    {
        Config = sim;
        Cert = dashboard.Certificate;
        InitialTotal = sim.Skills.Sum(s => s.BaseScaledScore);
        InitialBand = sim.Bands.FirstOrDefault(b => InitialTotal >= b.MinTotal && InitialTotal <= b.MaxTotal)
            ?? (InitialTotal < sim.Bands[0].MinTotal ? sim.Bands[0] : sim.Bands[^1]);

        TargetCode = sim.TargetBandCode ?? sim.Bands[^1].Code;
        TargetBandObj = sim.Bands.FirstOrDefault(b => string.Equals(b.Code, TargetCode, StringComparison.OrdinalIgnoreCase));
        TargetMin = sim.TargetMinScore ?? TargetBandObj?.MinTotal ?? sim.Bands[^1].MinTotal;
        TargetName = TargetBandObj?.Name ?? TargetCode;
        InitialDeficit = Math.Max(0, TargetMin - InitialTotal);
        HasExplicitTarget = !string.IsNullOrEmpty(dashboard.TargetReadiness?.TargetBandCode);

        TopLeverageSkill = sim.Skills
            .OrderByDescending(s => s.MaxScaledScore - s.BaseScaledScore)
            .ThenBy(s => s.SkillId)
            .First();
        TopHeadroom = Math.Max(0, TopLeverageSkill.MaxScaledScore - TopLeverageSkill.BaseScaledScore);

        BandsJson = JsonSerializer.Serialize(sim.Bands.Select(b => new
        {
            code = b.Code,
            name = b.Name,
            minTotal = b.MinTotal,
            maxTotal = b.MaxTotal
        }));

        Skills = sim.Skills.ToDictionary(s => s.SkillId, s => new SimulatorSkillPresentation(s));
        Bands = sim.Bands.ToDictionary(b => b.Code, b => new SimulatorBandPresentation(string.Equals(b.Code, InitialBand.Code, StringComparison.OrdinalIgnoreCase),
            !string.Equals(b.Code, InitialBand.Code, StringComparison.OrdinalIgnoreCase) && InitialTotal > b.MaxTotal, string.Equals(b.Code, TargetCode, StringComparison.OrdinalIgnoreCase)));
    }
}
