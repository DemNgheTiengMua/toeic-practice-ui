
namespace Contracts;

public sealed record SimulatorConfigDto(
    int MaxTotalScore,
    int? CurrentTotalScore,
    string? CurrentBandCode,
    string? TargetBandCode,
    int? TargetMinScore,
    IReadOnlyList<SimulatorSkillDto> Skills,
    IReadOnlyList<ScoreBandThresholdDto> Bands);
