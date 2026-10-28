
namespace BusinessObjects;

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
