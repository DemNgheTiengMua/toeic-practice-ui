using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class LearningPathEndpoints
{
    private static LearningPathSummaryDto ToSummaryDto(LearningPathSummaryView view) =>
        new(
            view.HasActivePath,
            ToCertificateDto(view.ActiveCert),
            view.LearnerPathId,
            view.FromBandCode,
            view.ToBandCode,
            view.FromBandName,
            view.ToBandName,
            view.CurrentBandCode,
            view.CurrentScore,
            view.TargetBandCode,
            view.TargetScore,
            view.Status?.MapTo<LearnerPathStatusDto>(),
            view.StartedAt,
            view.IsPremium,
            view.TotalModules,
            view.CompletedModules,
            view.TotalEstimatedMinutes,
            view.RemainingMinutes,
            view.CefrBands.Select(b => new CefrBandInfoDto(b.Code, b.Name, b.MinTotal, b.MaxTotal, b.IsReached, b.IsCurrent, b.IsTarget)).ToList(),
            view.Modules.Select(m => new LearningPathModuleSummaryDto(
                m.ModuleId,
                m.TemplateId,
                m.Title,
                m.GroupName,
                m.Description,
                m.EstimatedMinutes,
                m.DisplayOrder,
                m.SkillCode,
                m.SkillName,
                m.Modality.MapTo<ModalityDto>(),
                m.Status,
                m.IsLocked,
                m.CanContinue,
                m.TotalQuestions,
                m.CompletedQuestions,
                m.TotalItems,
                m.CompletedItems,
                m.ActiveStepId)).ToList(),
            view.NextModuleId);

    private static LearningPathModuleDetailDto ToDetailDto(LearningPathModuleDetailView detail) =>
        new(
            detail.LearnerPathId,
            detail.ModuleId,
            detail.Title,
            detail.GroupName,
            detail.Description,
            detail.EstimatedMinutes,
            detail.DisplayOrder,
            detail.SkillCode,
            detail.SkillName,
            detail.Modality.MapTo<ModalityDto>(),
            detail.Status,
            detail.IsLocked,
            detail.ModuleIndex,
            detail.TotalModules,
            detail.FromBandCode,
            detail.ToBandCode,
            detail.AnsweredQuestions,
            detail.TotalQuestions,
            detail.AccuracyPercentage,
            detail.CorrectCount,
            detail.TotalAnswered,
            detail.TimeSpentMinutes,
            detail.EstimatedMinutesRemaining,
            detail.IsPremium,
            detail.Topics.Select(t => new ModuleTopicDto(t.TopicId, t.Title, t.QuestionCount, t.Status, t.VideoPath, t.CanAccessVideo)).ToList(),
            detail.Steps.Select(s => new ModuleStepDto(
                s.StepId,
                s.ItemId,
                s.ItemType.MapTo<PathModuleItemTypeDto>(),
                s.Title,
                s.Status.MapTo<LearnerPathStepStatusDto>(),
                s.Score,
                s.QuestionCount,
                s.IsPremiumOnly,
                s.CanStart,
                s.StartedAt,
                s.CompletedAt,
                s.RefSectionId,
                s.RefExamId,
                s.RefLessonId)).ToList(),
            detail.ActiveStepId);
}
