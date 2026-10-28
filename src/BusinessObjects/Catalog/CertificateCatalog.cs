
namespace BusinessObjects;

/// <summary>The public catalogue: one certificate with its skills, sections and score bands.</summary>
public sealed record CertificateCatalog(
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<SkillCatalog> Skills,
    IReadOnlyList<BandCatalog> Bands,
    int CertId = 0,
    string ScoreAggregationRule = "sum",
    string? PresetCode = null,
    bool IsActive = true);
