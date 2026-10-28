using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record LearningPathModuleSummaryDto(
    int ModuleId,
    int TemplateId,
    string Title,
    string? GroupName,
    string? Description,
    int? EstimatedMinutes,
    int DisplayOrder,
    string SkillCode,
    string SkillName,
    ModalityDto Modality,
    string Status,
    bool IsLocked,
    bool CanContinue,
    int TotalQuestions,
    int CompletedQuestions,
    int TotalItems,
    int CompletedItems,
    int? ActiveStepId);
