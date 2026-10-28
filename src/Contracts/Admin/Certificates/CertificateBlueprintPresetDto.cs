
namespace Contracts;

/// <summary>Official blueprint presets for certificate configuration.</summary>
public sealed record CertificateBlueprintPresetDto(
    string PresetCode,
    string Name,
    string Description,
    string ScoreAggregationRule,
    IReadOnlyList<SkillBlueprintPresetDto> Skills);
