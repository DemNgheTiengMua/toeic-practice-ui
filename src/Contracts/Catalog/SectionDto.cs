
namespace Contracts;

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
