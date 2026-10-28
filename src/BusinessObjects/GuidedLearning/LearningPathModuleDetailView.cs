using System;
using System.Collections.Generic;

namespace BusinessObjects;

public sealed record LearningPathModuleDetailView(
    int LearnerPathId,
    int ModuleId,
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
    int ModuleIndex,
    int TotalModules,
    string FromBandCode,
    string ToBandCode,
    int AnsweredQuestions,
    int TotalQuestions,
    int? AccuracyPercentage,
    int CorrectCount,
    int TotalAnswered,
    int TimeSpentMinutes,
    int EstimatedMinutesRemaining,
    bool IsPremium,
    List<ModuleTopicInfo> Topics,
    List<ModuleStepInfo> Steps,
    int? ActiveStepId);
