
using System.Text.Json.Serialization;

namespace Contracts;

public sealed record SaveSectionWizardDto
{
    [JsonConstructor]
    public SaveSectionWizardDto(
        int? SectionId,
        string Code,
        string Name,
        int DisplayOrder,
        decimal ScoreWeight,
        NavigationModeDto? NavigationMode,
        string? TimerPolicyJson,
        LayoutTypeDto? LayoutType,
        int OptionCount,
        int ClusterSize,
        int UnitCount,
        bool HideOptionText,
        bool? RequirePassage,
        bool? RequireAudio,
        bool RequireImage,
        int RequiredKeywordCount,
        int ExpectedQuestionCount,
        string? AllowedComponentTypesJson,
        string? ToolsConfigJson)
    {
        this.SectionId = SectionId;
        this.Code = Code;
        this.Name = Name;
        this.DisplayOrder = DisplayOrder;
        this.ScoreWeight = ScoreWeight;
        this.NavigationMode = NavigationMode;
        this.TimerPolicyJson = TimerPolicyJson;
        this.LayoutType = LayoutType;
        this.OptionCount = OptionCount;
        this.ClusterSize = ClusterSize;
        this.UnitCount = UnitCount;
        this.HideOptionText = HideOptionText;
        this.RequirePassage = RequirePassage;
        this.RequireAudio = RequireAudio;
        this.RequireImage = RequireImage;
        this.RequiredKeywordCount = RequiredKeywordCount;
        this.ExpectedQuestionCount = ExpectedQuestionCount;
        this.AllowedComponentTypesJson = AllowedComponentTypesJson;
        this.ToolsConfigJson = ToolsConfigJson;
    }

    public SaveSectionWizardDto(string Code, string Name, int ExpectedQuestionCount)
        : this(
            SectionId: null,
            Code: Code,
            Name: Name,
            DisplayOrder: 0,
            ScoreWeight: 1.0m,
            NavigationMode: null,
            TimerPolicyJson: null,
            LayoutType: null,
            OptionCount: 4,
            ClusterSize: 1,
            UnitCount: 1,
            HideOptionText: false,
            RequirePassage: null,
            RequireAudio: null,
            RequireImage: false,
            RequiredKeywordCount: 0,
            ExpectedQuestionCount: ExpectedQuestionCount,
            AllowedComponentTypesJson: null,
            ToolsConfigJson: null)
    {
    }

    public int? SectionId { get; init; }
    public string Code { get; init; }
    public string Name { get; init; }
    public int DisplayOrder { get; init; }
    public decimal ScoreWeight { get; init; }
    public NavigationModeDto? NavigationMode { get; init; }
    public string? TimerPolicyJson { get; init; }
    public LayoutTypeDto? LayoutType { get; init; }
    public int OptionCount { get; init; }
    public int ClusterSize { get; init; }
    public int UnitCount { get; init; }
    public bool HideOptionText { get; init; }
    public bool? RequirePassage { get; init; }
    public bool? RequireAudio { get; init; }
    public bool RequireImage { get; init; }
    public int RequiredKeywordCount { get; init; }
    public int ExpectedQuestionCount { get; init; }
    public string? AllowedComponentTypesJson { get; init; }
    public string? ToolsConfigJson { get; init; }
}
