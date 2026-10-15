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

public sealed record LearningPathSummaryView(
    bool HasActivePath,
    CertificateOption ActiveCert,
    int? LearnerPathId,
    string? FromBandCode,
    string? ToBandCode,
    string? FromBandName,
    string? ToBandName,
    string? CurrentBandCode,
    int? CurrentScore,
    string? TargetBandCode,
    int? TargetScore,
    LearnerPathStatus? Status,
    DateTimeOffset? StartedAt,
    bool IsPremium,
    int TotalModules,
    int CompletedModules,
    int TotalEstimatedMinutes,
    int RemainingMinutes,
    List<CefrBandInfo> CefrBands,
    List<LearningPathModuleSummary> Modules,
    int? NextModuleId);

public sealed record ModuleTopicInfo(
    int TopicId,
    string Title,
    int? QuestionCount,
    string Status);

public sealed record ModuleStepInfo(
    int StepId,
    int ItemId,
    PathModuleItemType ItemType,
    string Title,
    LearnerPathStepStatus Status,
    decimal? Score,
    int? QuestionCount,
    bool IsPremiumOnly,
    bool CanStart,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int? RefSectionId,
    int? RefExamId);

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

public sealed record StartStepResult(
    int StepId,
    PathModuleItemType ItemType,
    string RedirectUrl,
    int? SessionId,
    int? AttemptId);
