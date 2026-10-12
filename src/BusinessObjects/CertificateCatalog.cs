namespace BusinessObjects;

/// <summary>The public catalogue: one certificate with its skills, sections and score bands.</summary>
public sealed record CertificateCatalog(
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<SkillCatalog> Skills,
    IReadOnlyList<BandCatalog> Bands);

public sealed record SkillCatalog(
    string Code,
    string Name,
    string Modality,
    int? DurationMinutes,
    IReadOnlyList<SectionCatalog> Sections);

public sealed record SectionCatalog(
    string Code,
    string Name,
    int? ExpectedQuestionCount);

public sealed record BandCatalog(
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal);
