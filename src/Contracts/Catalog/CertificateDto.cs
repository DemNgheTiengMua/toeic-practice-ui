
namespace Contracts;

/// <summary>Wire DTOs for the certificates endpoint. JSON-serialized by the API, deserialized by the MVC site.</summary>
public sealed record CertificateDto(
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<SkillDto> Skills,
    IReadOnlyList<BandDto> Bands,
    int CertId = 0,
    string ScoreAggregationRule = "sum",
    string? PresetCode = null,
    bool IsActive = true);
