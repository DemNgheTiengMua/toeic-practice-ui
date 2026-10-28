using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record ExamStudioSectionDto(
    int SectionId,
    int CertId,
    string Code,
    string Name,
    ModalityDto Modality,
    int? ExpectedQuestionCount,
    int DisplayOrder,
    int CurrentQuestionCount,
    bool HasAudio,
    string? MasterAudioPath,
    int? MasterAudioDurationSec,
    IReadOnlyList<ExamStudioGroupDto> Groups,
    int OptionCount = 4,
    int ClusterSize = 1,
    int UnitCount = 1,
    bool HideOptionText = false,
    bool RequirePassage = false,
    bool RequireAudio = false,
    bool RequireImage = false,
    int RequiredKeywordCount = 0,
    string? AllowedComponentTypesJson = null,
    string LayoutType = "STANDARD_VERTICAL",
    string NavigationMode = "FULL_SANDBOX",
    string? TimerPolicyJson = null,
    string? ToolsConfigJson = null,
    int SkillId = 0,
    string? SkillCode = null,
    string? SkillName = null);
