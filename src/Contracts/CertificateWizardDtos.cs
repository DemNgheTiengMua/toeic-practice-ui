namespace Contracts;

/// <summary>Official blueprint presets for certificate configuration.</summary>
public sealed record CertificateBlueprintPresetDto(
    string PresetCode,
    string Name,
    string Description,
    string ScoreAggregationRule,
    IReadOnlyList<SkillBlueprintPresetDto> Skills);

public sealed record SkillBlueprintPresetDto(
    string Code,
    string Name,
    ModalityDto Modality,
    int DisplayOrder,
    int? DurationMinutes,
    IReadOnlyList<SectionBlueprintPresetDto> Sections);

public sealed record SectionBlueprintPresetDto(
    string Code,
    string Name,
    int DisplayOrder,
    decimal ScoreWeight,
    NavigationModeDto NavigationMode,
    string? TimerPolicyJson,
    LayoutTypeDto LayoutType,
    int OptionCount,
    int ClusterSize,
    int UnitCount,
    bool HideOptionText,
    bool RequirePassage,
    bool RequireAudio,
    bool RequireImage,
    int RequiredKeywordCount,
    int? ExpectedQuestionCount,
    string? AllowedComponentTypesJson,
    string? ToolsConfigJson);

/// <summary>Request to create or update a certificate via the linear wizard.</summary>
public sealed record SaveCertificateWizardRequest(
    int? CertId,
    string Code,
    string Name,
    string? Description,
    string ScoreAggregationRule,
    string? PresetCode,
    bool IsActive,
    IReadOnlyList<SaveSkillWizardDto> Skills);

public sealed record SaveSkillWizardDto(
    int? SkillId,
    string Code,
    string Name,
    ModalityDto Modality,
    int DisplayOrder,
    int? DurationMinutes,
    IReadOnlyList<SaveSectionWizardDto> Sections);

public sealed record SaveSectionWizardDto(
    int? SectionId,
    string Code,
    string Name,
    int DisplayOrder,
    decimal ScoreWeight,
    NavigationModeDto NavigationMode,
    string? TimerPolicyJson,
    LayoutTypeDto LayoutType,
    int OptionCount,
    int ClusterSize,
    int UnitCount,
    bool HideOptionText,
    bool RequirePassage,
    bool RequireAudio,
    bool RequireImage,
    int RequiredKeywordCount,
    int? ExpectedQuestionCount,
    string? AllowedComponentTypesJson,
    string? ToolsConfigJson);

public sealed record SaveCertificateWizardResultDto(
    int CertId,
    string Code,
    string Name,
    int SkillsCount,
    int SectionsCount);

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

public sealed record SkillBlueprintDetailDto(
    int SkillId,
    int CertId,
    string Code,
    string Name,
    ModalityDto Modality,
    int DisplayOrder,
    int? DurationMinutes,
    IReadOnlyList<SectionBlueprintDetailDto> Sections);

public sealed record SectionBlueprintDetailDto(
    int SectionId,
    int SkillId,
    int CertId,
    string Code,
    string Name,
    int DisplayOrder,
    decimal ScoreWeight,
    NavigationModeDto NavigationMode,
    string? TimerPolicyJson,
    LayoutTypeDto LayoutType,
    int OptionCount,
    int ClusterSize,
    int UnitCount,
    bool HideOptionText,
    bool RequirePassage,
    bool RequireAudio,
    bool RequireImage,
    int RequiredKeywordCount,
    int? ExpectedQuestionCount,
    string? AllowedComponentTypesJson,
    string? ToolsConfigJson);

/// <summary>Summary of a certificate for admin list view.</summary>
public sealed record AdminCertificateSummaryDto(
    int CertId,
    string Code,
    string Name,
    string? Description,
    string ScoreAggregationRule,
    string? PresetCode,
    bool IsActive,
    int SkillsCount,
    int SectionsCount,
    int ExamsCount);
