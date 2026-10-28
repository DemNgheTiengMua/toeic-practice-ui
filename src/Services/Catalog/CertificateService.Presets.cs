using System.Text;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class CertificateService
{

    public IReadOnlyList<CertificateBlueprintPresetDto> GetOfficialPresets()
    {
        return
        [
            // 1. IELTS Academic
            new CertificateBlueprintPresetDto(
                PresetCode: "ielts_academic",
                Name: "IELTS Academic",
                Description: "International English Language Testing System (Academic) - 4 skills, 0.5 step scoring (0.0 to 9.0). Listening features audio preview and 2-minute post-audio review countdown; Reading and Writing use horizontal split-screen; Writing weights Task 2 at 2:1; Speaking is auto-paced.",
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
                                Code: "REC",
                                Name: "Four recordings: everyday conversation to academic lecture",
                                DisplayOrder: 1,
                                ScoreWeight: 1.0m,
                                NavigationMode: NavigationModeDto.AudioDriven,
                                TimerPolicyJson: "{\"timerMode\":\"AUDIO_PLUS_CHECK\",\"reviewSeconds\":120}",
                                LayoutType: LayoutTypeDto.SplitHorizontal,
                                OptionCount: 4,
                                ClusterSize: 1,
                                UnitCount: 1,
                                HideOptionText: false,
                                RequirePassage: false,
                                RequireAudio: true,
                                RequireImage: false,
                                RequiredKeywordCount: 0,
                                ExpectedQuestionCount: 40,
                                AllowedComponentTypesJson: null,
                                ToolsConfigJson: "{\"highlighter\":true,\"notes\":true}")
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
                                Code: "PAS",
                                Name: "Three long passages with a mix of question types",
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
                                ExpectedQuestionCount: 40,
                                AllowedComponentTypesJson: null,
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
                                Code: "T1",
                                Name: "Task 1 — Describe a chart (150 words)",
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
                                RequireImage: true,
                                RequiredKeywordCount: 0,
                                ExpectedQuestionCount: 1,
                                AllowedComponentTypesJson: "[\"free_response\"]",
                                ToolsConfigJson: "{\"wordCount\":true,\"disableSpellcheck\":true}"),
                            new SectionBlueprintPresetDto(
                                Code: "T2",
                                Name: "Task 2 — Write an essay (250 words)",
                                DisplayOrder: 2,
                                ScoreWeight: 2.0m,
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
                                ExpectedQuestionCount: 1,
                                AllowedComponentTypesJson: "[\"free_response\"]",
                                ToolsConfigJson: "{\"wordCount\":true,\"disableSpellcheck\":true}")
                        ]),
                    new SkillBlueprintPresetDto(
                        Code: "SPEA",
                        Name: "Speaking",
                        Modality: ModalityDto.Speaking,
                        DisplayOrder: 4,
                        DurationMinutes: 14,
                        Sections:
                        [
                            new SectionBlueprintPresetDto(
                                Code: "INT",
                                Name: "Interview, cue-card talk and discussion",
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
                ]),

            // 2. TOEIC Listening & Reading (Standard CBT)
            new CertificateBlueprintPresetDto(
                PresetCode: "toeic_lr_std",
                Name: "TOEIC Listening & Reading (Standard CBT)",
                Description: "Standard 200 questions across 7 parts. Listening is audio-driven with option text hidden for Parts 1 & 2, and 3-question clusters for Parts 3 & 4. Reading is full sandbox single-column.",
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
                            new SectionBlueprintPresetDto("P1", "Part 1: Photographs", 1, 1.0m, NavigationModeDto.AudioDriven, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, true, false, true, true, 0, 6, "[\"mcq\"]", null),
                            new SectionBlueprintPresetDto("P2", "Part 2: Question-Response", 2, 1.0m, NavigationModeDto.AudioDriven, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 3, 1, 1, true, false, true, false, 0, 25, "[\"mcq_3\"]", null),
                            new SectionBlueprintPresetDto("P3", "Part 3: Conversations", 3, 1.0m, NavigationModeDto.AudioDriven, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 3, 1, false, false, true, false, 0, 39, "[\"mcq\"]", null),
                            new SectionBlueprintPresetDto("P4", "Part 4: Talks", 4, 1.0m, NavigationModeDto.AudioDriven, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 3, 1, false, false, true, false, 0, 30, "[\"mcq\"]", null)
                        ]),
                    new SkillBlueprintPresetDto(
                        Code: "READ",
                        Name: "Reading",
                        Modality: ModalityDto.Reading,
                        DisplayOrder: 2,
                        DurationMinutes: 75,
                        Sections:
                        [
                            new SectionBlueprintPresetDto("P5", "Part 5: Incomplete Sentences", 1, 1.0m, NavigationModeDto.FullSandbox, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, false, false, 0, 30, "[\"mcq\"]", null),
                            new SectionBlueprintPresetDto("P6", "Part 6: Text Completion", 2, 1.0m, NavigationModeDto.FullSandbox, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 4, 1, false, true, false, false, 0, 16, "[\"mcq\"]", null),
                            new SectionBlueprintPresetDto("P7", "Part 7: Reading Comprehension", 3, 1.0m, NavigationModeDto.FullSandbox, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, true, false, false, 0, 54, "[\"mcq\"]", null)
                        ])
                ]),

            // 3. TOEIC Listening & Reading (Adaptive CAT)
            new CertificateBlueprintPresetDto(
                PresetCode: "toeic_lr_cat",
                Name: "TOEIC Listening & Reading (Adaptive CAT)",
                Description: "Two-stage computer adaptive test. Listening is audio-driven. Reading uses unit locking (UNIT_LOCKED_SANDBOX, UnitCount = 2) where candidates cannot return to Unit 1 once advancing to Unit 2.",
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
                            new SectionBlueprintPresetDto("LIST_CAT", "Listening Sections (Parts 1-4)", 1, 1.0m, NavigationModeDto.AudioDriven, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, true, false, 0, 50, "[\"mcq\",\"mcq_3\"]", null)
                        ]),
                    new SkillBlueprintPresetDto(
                        Code: "READ",
                        Name: "Reading",
                        Modality: ModalityDto.Reading,
                        DisplayOrder: 2,
                        DurationMinutes: 45,
                        Sections:
                        [
                            new SectionBlueprintPresetDto("READ_CAT", "Adaptive Reading (Unit 1 & Unit 2)", 1, 1.0m, NavigationModeDto.UnitLockedSandbox, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 1, 2, false, true, false, false, 0, 50, "[\"mcq\"]", null)
                        ])
                ]),

            // 4. TOEIC Speaking & Writing
            new CertificateBlueprintPresetDto(
                PresetCode: "toeic_sw",
                Name: "TOEIC Speaking & Writing",
                Description: "Speaking consists of 11 auto-paced questions. Writing operates in hybrid group-locked mode with 5 picture-based sentence questions requiring 2 keywords, 2 written requests, and 1 opinion essay with on-screen cut/paste tools.",
                ScoreAggregationRule: "independent",
                Skills:
                [
                    new SkillBlueprintPresetDto(
                        Code: "SPEA",
                        Name: "Speaking",
                        Modality: ModalityDto.Speaking,
                        DisplayOrder: 1,
                        DurationMinutes: 20,
                        Sections:
                        [
                            new SectionBlueprintPresetDto("S1", "Questions 1-2: Read a text aloud", 1, 1.0m, NavigationModeDto.SystemPacedAuto, "{\"timerMode\":\"ITEM_FIXED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, false, false, 0, 2, "[\"free_response\"]", null),
                            new SectionBlueprintPresetDto("S2", "Questions 3-4: Describe a picture", 2, 1.0m, NavigationModeDto.SystemPacedAuto, "{\"timerMode\":\"ITEM_FIXED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, false, true, 0, 2, "[\"free_response\"]", null),
                            new SectionBlueprintPresetDto("S3", "Questions 5-7: Respond to questions", 3, 1.0m, NavigationModeDto.SystemPacedAuto, "{\"timerMode\":\"ITEM_FIXED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, true, false, 0, 3, "[\"free_response\"]", null),
                            new SectionBlueprintPresetDto("S4", "Questions 8-10: Respond using information provided", 4, 1.0m, NavigationModeDto.SystemPacedAuto, "{\"timerMode\":\"ITEM_FIXED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, true, true, false, 0, 3, "[\"free_response\"]", null),
                            new SectionBlueprintPresetDto("S5", "Question 11: Express an opinion", 5, 1.0m, NavigationModeDto.SystemPacedAuto, "{\"timerMode\":\"ITEM_FIXED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, true, false, 0, 1, "[\"free_response\"]", null)
                        ]),
                    new SkillBlueprintPresetDto(
                        Code: "WRIT",
                        Name: "Writing",
                        Modality: ModalityDto.Writing,
                        DisplayOrder: 2,
                        DurationMinutes: 60,
                        Sections:
                        [
                            new SectionBlueprintPresetDto("W1", "Questions 1-5: Write a sentence based on a picture", 1, 1.0m, NavigationModeDto.HybridGroupLocked, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, false, true, 2, 5, "[\"free_response\"]", "{\"cutPaste\":true,\"wordCount\":true,\"disableSpellcheck\":true}"),
                            new SectionBlueprintPresetDto("W2", "Questions 6-7: Respond to a written request", 2, 1.0m, NavigationModeDto.HybridGroupLocked, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, true, false, false, 0, 2, "[\"free_response\"]", "{\"cutPaste\":true,\"wordCount\":true,\"disableSpellcheck\":true}"),
                            new SectionBlueprintPresetDto("W3", "Question 8: Write an opinion essay", 3, 2.0m, NavigationModeDto.HybridGroupLocked, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, false, false, 0, 1, "[\"free_response\"]", "{\"cutPaste\":true,\"wordCount\":true,\"disableSpellcheck\":true}")
                        ])
                ]),

            // 5. VSTEP (B1-B2-C1)
            new CertificateBlueprintPresetDto(
                PresetCode: "vstep",
                Name: "VSTEP (B1-B2-C1)",
                Description: "Vietnamese Standardized Test of English Proficiency (Levels 3 to 5 / B1 to C1). Listening is audio-driven; Reading and Writing use horizontal split-screen; Speaking is auto-paced. Scoring aggregated to average whole band.",
                ScoreAggregationRule: "avg_whole",
                Skills:
                [
                    new SkillBlueprintPresetDto(
                        Code: "LIST",
                        Name: "Listening",
                        Modality: ModalityDto.Listening,
                        DisplayOrder: 1,
                        DurationMinutes: 40,
                        Sections:
                        [
                            new SectionBlueprintPresetDto("VSTEP_LIST", "Listening Parts 1-3", 1, 1.0m, NavigationModeDto.AudioDriven, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, true, false, 0, 35, "[\"mcq\"]", null)
                        ]),
                    new SkillBlueprintPresetDto(
                        Code: "READ",
                        Name: "Reading",
                        Modality: ModalityDto.Reading,
                        DisplayOrder: 2,
                        DurationMinutes: 60,
                        Sections:
                        [
                            new SectionBlueprintPresetDto("VSTEP_READ", "Reading Passages 1-4", 1, 1.0m, NavigationModeDto.FullSandbox, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.SplitHorizontal, 4, 1, 1, false, true, false, false, 0, 40, "[\"mcq\"]", "{\"highlighter\":true,\"notes\":true}")
                        ]),
                    new SkillBlueprintPresetDto(
                        Code: "WRIT",
                        Name: "Writing",
                        Modality: ModalityDto.Writing,
                        DisplayOrder: 3,
                        DurationMinutes: 60,
                        Sections:
                        [
                            new SectionBlueprintPresetDto("VSTEP_WRIT", "Writing Tasks 1 & 2", 1, 1.0m, NavigationModeDto.FullSandbox, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.SplitHorizontal, 4, 1, 1, false, false, false, false, 0, 2, "[\"free_response\"]", "{\"wordCount\":true,\"disableSpellcheck\":true}")
                        ]),
                    new SkillBlueprintPresetDto(
                        Code: "SPEA",
                        Name: "Speaking",
                        Modality: ModalityDto.Speaking,
                        DisplayOrder: 4,
                        DurationMinutes: 12,
                        Sections:
                        [
                            new SectionBlueprintPresetDto("VSTEP_SPEA", "Speaking Parts 1-3", 1, 1.0m, NavigationModeDto.SystemPacedAuto, "{\"timerMode\":\"ITEM_FIXED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, false, false, 0, 3, "[\"free_response\"]", null)
                        ])
                ]),

            // 6. Custom Template
            new CertificateBlueprintPresetDto(
                PresetCode: "custom",
                Name: "Custom Certificate",
                Description: "Blank template to configure custom skills, sections, navigation modes, timers, and authoring rules.",
                ScoreAggregationRule: "sum",
                Skills:
                [
                    new SkillBlueprintPresetDto(
                        Code: "SKILL1",
                        Name: "Section/Skill 1",
                        Modality: ModalityDto.Listening,
                        DisplayOrder: 1,
                        DurationMinutes: 30,
                        Sections:
                        [
                            new SectionBlueprintPresetDto("SEC1", "Section 1", 1, 1.0m, NavigationModeDto.FullSandbox, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, false, false, 0, 10, null, null)
                        ])
                ])
        ];
    }
}
