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

public sealed record SkillDto(
    string Code,
    string Name,
    ModalityDto Modality,
    int? DurationMinutes,
    IReadOnlyList<SectionDto> Sections,
    int SkillId = 0,
    int DisplayOrder = 1);

public sealed record SectionDto(
    string Code,
    string Name,
    int? ExpectedQuestionCount,
    int SectionId = 0,
    NavigationModeDto NavigationMode = NavigationModeDto.FullSandbox,
    decimal ScoreWeight = 1.0m,
    string? TimerPolicyJson = null,
    LayoutTypeDto LayoutType = LayoutTypeDto.StandardVertical,
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

public sealed record BandDto(
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal);
