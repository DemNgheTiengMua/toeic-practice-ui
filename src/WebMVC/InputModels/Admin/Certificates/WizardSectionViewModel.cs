using Contracts;

namespace WebMVC.Models;

public sealed class WizardSectionViewModel
{

    public int? SectionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; } = 1;
    public decimal ScoreWeight { get; set; } = 1.0m;
    public NavigationModeDto NavigationMode { get; set; } = NavigationModeDto.FullSandbox;
    public string? TimerPolicyJson { get; set; }
    public LayoutTypeDto LayoutType { get; set; } = LayoutTypeDto.StandardVertical;
    public int OptionCount { get; set; } = 4;
    public int ClusterSize { get; set; } = 1;
    public int UnitCount { get; set; } = 1;
    public bool HideOptionText { get; set; }
    public bool RequirePassage { get; set; }
    public bool RequireAudio { get; set; }
    public bool RequireImage { get; set; }
    public int RequiredKeywordCount { get; set; }
    public int? ExpectedQuestionCount { get; set; }
    public string? AllowedComponentTypesJson { get; set; }
    public string? ToolsConfigJson { get; set; }

    // Helper properties for toggles and UI
    public string TimerMode { get; set; } = "SECTION_SHARED";
    public int? ReviewSeconds { get; set; }
    public bool ToolHighlighter { get; set; }
    public bool ToolNotes { get; set; }
    public bool ToolWordCount { get; set; }
    public bool ToolDisableSpellcheck { get; set; }
    public bool ToolCutPaste { get; set; }

    // Question components enabled
    public bool ComponentMcq { get; set; } = true;
    public bool ComponentMcq3 { get; set; }
    public bool ComponentMultiSelect { get; set; }
    public bool ComponentInlineCompletion { get; set; }
    public bool ComponentDropdown { get; set; }
    public bool ComponentDragDrop { get; set; }
    public bool ComponentFreeResponse { get; set; }
}
