
namespace Contracts;

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
