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

public sealed record LearningPathSummaryDto(
    bool HasActivePath,
    CertificateOptionDto ActiveCert,
    int? LearnerPathId,
    string? FromBandCode,
    string? ToBandCode,
    string? FromBandName,
    string? ToBandName,
    string? CurrentBandCode,
    int? CurrentScore,
    string? TargetBandCode,
    int? TargetScore,
    LearnerPathStatusDto? Status,
    DateTimeOffset? StartedAt,
    bool IsPremium,
    int TotalModules,
    int CompletedModules,
    int TotalEstimatedMinutes,
    int RemainingMinutes,
    List<CefrBandInfoDto> CefrBands,
    List<LearningPathModuleSummaryDto> Modules,
    int? NextModuleId);

public sealed record ModuleTopicDto(
    int TopicId,
    string Title,
    int? QuestionCount,
    string Status);

public sealed record ModuleStepDto(
    int StepId,
    int ItemId,
    PathModuleItemTypeDto ItemType,
    string Title,
    LearnerPathStepStatusDto Status,
    decimal? Score,
    int? QuestionCount,
    bool IsPremiumOnly,
    bool CanStart,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int? RefSectionId,
    int? RefExamId);

public sealed record LearningPathModuleDetailDto(
    int LearnerPathId,
    int ModuleId,
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
    List<ModuleTopicDto> Topics,
    List<ModuleStepDto> Steps,
    int? ActiveStepId);

public sealed record StartStepResultDto(
    int StepId,
    PathModuleItemTypeDto ItemType,
    string RedirectUrl,
    int? SessionId,
    int? AttemptId);

public sealed record GenerateLearningPathRequest(
    string? TargetBandCode = null);

public sealed record ResetLearningPathResultDto(
    bool Success);
