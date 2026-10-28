
namespace BusinessObjects;

public sealed record SimulatorConfig(
    int MaxTotalScore,
    int? CurrentTotalScore,
    string? CurrentBandCode,
    string? TargetBandCode,
    int? TargetMinScore,
    IReadOnlyList<SimulatorSkill> Skills,
    IReadOnlyList<ScoreBandThreshold> Bands);
