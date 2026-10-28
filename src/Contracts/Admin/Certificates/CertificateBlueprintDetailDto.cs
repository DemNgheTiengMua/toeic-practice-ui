
namespace Contracts;

/// <summary>Complete blueprint detail for downstream Exam Studio, Quick Ingest, and Sittings.</summary>
public sealed record CertificateBlueprintDetailDto(
    int CertId,
    string Code,
    string Name,
    string? Description,
    string ScoreAggregationRule,
    string? PresetCode,
    bool IsActive,
    IReadOnlyList<SkillBlueprintDetailDto> Skills,
    IReadOnlyList<BandDto> Bands);
