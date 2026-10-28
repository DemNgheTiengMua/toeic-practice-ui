using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record LearningPathModuleSummary(
    int ModuleId,
    int TemplateId,
    string Title,
    string? GroupName,
    string? Description,
    int? EstimatedMinutes,
    int DisplayOrder,
    string SkillCode,
    string SkillName,
    Modality Modality,
    string Status,
    bool IsLocked,
    bool CanContinue,
    int TotalQuestions,
    int CompletedQuestions,
    int TotalItems,
    int CompletedItems,
    int? ActiveStepId);
