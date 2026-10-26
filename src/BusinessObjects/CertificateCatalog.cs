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

public sealed record SkillCatalog(
    string Code,
    string Name,
    Modality Modality,
    int? DurationMinutes,
    IReadOnlyList<SectionCatalog> Sections,
    int SkillId = 0,
    int DisplayOrder = 1);

public sealed record SectionCatalog(
    string Code,
    string Name,
    int? ExpectedQuestionCount,
    int SectionId = 0,
    string NavigationMode = "FULL_SANDBOX",
    decimal ScoreWeight = 1.0m,
    string? TimerPolicyJson = null,
    string LayoutType = "STANDARD_VERTICAL",
    int OptionCount = 4,
    int ClusterSize = 1,
    int UnitCount = 1,
    bool HideOptionText = false,
    bool RequirePassage = false,
    bool RequireAudio = false,
    bool RequireImage = false,
    int RequiredKeywordCount = 0,
    string? AllowedComponentTypesJson = null,
    string? ToolsConfigJson = null,
    int DisplayOrder = 1);

public sealed record BandCatalog(
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal);
