using Contracts;

namespace Services;

public sealed partial class CertificateService
{
    public IReadOnlyList<CertificateBlueprintPresetDto> GetOfficialPresets()
    {
        return
        [
            new CertificateBlueprintPresetDto(
                PresetCode: "two_skill",
                Name: "2-Skill (Listening & Reading)",
                Description: "Streamlined 2-skill template (Listening & Reading) with automatic modality defaults and linear score scaling.",
                ScoreAggregationRule: "sum",
                Skills:
                [
                    new SkillBlueprintPresetDto(
                        Code: "LIST",
                        Name: "Listening",
                        Modality: ModalityDto.Listening,
                        DisplayOrder: 1,
                        DurationMinutes: 45,
                        Sections:
                        [
                            new SectionBlueprintPresetDto(
                                Code: "L1",
                                Name: "Listening Comprehension",
                                DisplayOrder: 1,
                                ScoreWeight: 1.0m,
                                NavigationMode: NavigationModeDto.AudioDriven,
                                TimerPolicyJson: "{\"timerMode\":\"AUDIO_PLUS_CHECK\",\"reviewSeconds\":120}",
                                LayoutType: LayoutTypeDto.StandardVertical,
                                OptionCount: 4,
                                ClusterSize: 1,
                                UnitCount: 1,
                                HideOptionText: false,
                                RequirePassage: false,
                                RequireAudio: true,
                                RequireImage: false,
                                RequiredKeywordCount: 0,
                                ExpectedQuestionCount: 25,
                                AllowedComponentTypesJson: "[\"mcq\"]",
                                ToolsConfigJson: null)
                        ]),
                    new SkillBlueprintPresetDto(
                        Code: "READ",
                        Name: "Reading",
                        Modality: ModalityDto.Reading,
                        DisplayOrder: 2,
                        DurationMinutes: 75,
                        Sections:
                        [
                            new SectionBlueprintPresetDto(
                                Code: "R1",
                                Name: "Reading Comprehension",
                                DisplayOrder: 1,
                                ScoreWeight: 1.0m,
                                NavigationMode: NavigationModeDto.FullSandbox,
                                TimerPolicyJson: "{\"timerMode\":\"SECTION_SHARED\"}",
                                LayoutType: LayoutTypeDto.SplitHorizontal,
                                OptionCount: 4,
                                ClusterSize: 1,
                                UnitCount: 1,
                                HideOptionText: false,
                                RequirePassage: true,
                                RequireAudio: false,
                                RequireImage: false,
                                RequiredKeywordCount: 0,
                                ExpectedQuestionCount: 25,
                                AllowedComponentTypesJson: "[\"mcq\"]",
                                ToolsConfigJson: "{\"highlighter\":true,\"notes\":true}")
                        ])
                ]),
            new CertificateBlueprintPresetDto(
                PresetCode: "four_skill",
                Name: "4-Skill (Listening, Reading, Speaking, Writing)",
                Description: "Streamlined 4-skill template (L/R/S/W) with automatic modality defaults, linear score scaling, and flat AI grading criteria.",
                ScoreAggregationRule: "avg_half_step",
                Skills:
                [
                    new SkillBlueprintPresetDto(
                        Code: "LIST",
                        Name: "Listening",
                        Modality: ModalityDto.Listening,
                        DisplayOrder: 1,
                        DurationMinutes: 30,
                        Sections:
                        [
                            new SectionBlueprintPresetDto(
                                Code: "L1",
                                Name: "Listening Recordings",
                                DisplayOrder: 1,
                                ScoreWeight: 1.0m,
                                NavigationMode: NavigationModeDto.AudioDriven,
                                TimerPolicyJson: "{\"timerMode\":\"AUDIO_PLUS_CHECK\",\"reviewSeconds\":120}",
                                LayoutType: LayoutTypeDto.StandardVertical,
                                OptionCount: 4,
                                ClusterSize: 1,
                                UnitCount: 1,
                                HideOptionText: false,
                                RequirePassage: false,
                                RequireAudio: true,
                                RequireImage: false,
                                RequiredKeywordCount: 0,
                                ExpectedQuestionCount: 20,
                                AllowedComponentTypesJson: "[\"mcq\"]",
                                ToolsConfigJson: null)
                        ]),
                    new SkillBlueprintPresetDto(
                        Code: "READ",
                        Name: "Reading",
                        Modality: ModalityDto.Reading,
                        DisplayOrder: 2,
                        DurationMinutes: 60,
                        Sections:
                        [
                            new SectionBlueprintPresetDto(
                                Code: "R1",
                                Name: "Reading Passages",
                                DisplayOrder: 1,
                                ScoreWeight: 1.0m,
                                NavigationMode: NavigationModeDto.FullSandbox,
                                TimerPolicyJson: "{\"timerMode\":\"SECTION_SHARED\"}",
                                LayoutType: LayoutTypeDto.SplitHorizontal,
                                OptionCount: 4,
                                ClusterSize: 1,
                                UnitCount: 1,
                                HideOptionText: false,
                                RequirePassage: true,
                                RequireAudio: false,
                                RequireImage: false,
                                RequiredKeywordCount: 0,
                                ExpectedQuestionCount: 20,
                                AllowedComponentTypesJson: "[\"mcq\"]",
                                ToolsConfigJson: "{\"highlighter\":true,\"notes\":true}")
                        ]),
                    new SkillBlueprintPresetDto(
                        Code: "WRIT",
                        Name: "Writing",
                        Modality: ModalityDto.Writing,
                        DisplayOrder: 3,
                        DurationMinutes: 60,
                        Sections:
                        [
                            new SectionBlueprintPresetDto(
                                Code: "W1",
                                Name: "Writing Task",
                                DisplayOrder: 1,
                                ScoreWeight: 1.0m,
                                NavigationMode: NavigationModeDto.FullSandbox,
                                TimerPolicyJson: "{\"timerMode\":\"SECTION_SHARED\"}",
                                LayoutType: LayoutTypeDto.SplitHorizontal,
                                OptionCount: 4,
                                ClusterSize: 1,
                                UnitCount: 1,
                                HideOptionText: false,
                                RequirePassage: false,
                                RequireAudio: false,
                                RequireImage: false,
                                RequiredKeywordCount: 0,
                                ExpectedQuestionCount: 2,
                                AllowedComponentTypesJson: "[\"free_response\"]",
                                ToolsConfigJson: "{\"wordCount\":true,\"disableSpellcheck\":true}")
                        ]),
                    new SkillBlueprintPresetDto(
                        Code: "SPEA",
                        Name: "Speaking",
                        Modality: ModalityDto.Speaking,
                        DisplayOrder: 4,
                        DurationMinutes: 15,
                        Sections:
                        [
                            new SectionBlueprintPresetDto(
                                Code: "S1",
                                Name: "Speaking Interview",
                                DisplayOrder: 1,
                                ScoreWeight: 1.0m,
                                NavigationMode: NavigationModeDto.SystemPacedAuto,
                                TimerPolicyJson: "{\"timerMode\":\"ITEM_FIXED\"}",
                                LayoutType: LayoutTypeDto.StandardVertical,
                                OptionCount: 4,
                                ClusterSize: 1,
                                UnitCount: 1,
                                HideOptionText: false,
                                RequirePassage: false,
                                RequireAudio: false,
                                RequireImage: false,
                                RequiredKeywordCount: 0,
                                ExpectedQuestionCount: 3,
                                AllowedComponentTypesJson: "[\"free_response\"]",
                                ToolsConfigJson: null)
                        ])
                ])
        ];
    }
}
