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
            view.TotalSteps,
            view.CompletedSteps,
            view.TotalEstimatedMinutes,
            view.RemainingMinutes,
            view.CefrBands.Select(b => new CefrBandInfoDto(b.Code, b.Name, b.MinTotal, b.MaxTotal, b.IsReached, b.IsCurrent, b.IsTarget)).ToList(),
            view.OpenSteps.Select(ToStepDto).ToList(),
            view.NextStepId,
            view.Weeks.Select(w => new LearningPathWeekSummaryDto(
                w.WeekNumber,
                w.PlannedSteps,
                w.CompletedSteps,
                w.PlannedMinutes,
                w.CompletedMinutes,
                w.IsOnTrack,
                w.TargetAccuracyPct,
                w.ActualAccuracyPct,
                w.Steps.Select(ToStepDto).ToList())).ToList(),
            view.SkillLevels.ToDictionary(kv => kv.Key, kv => kv.Value.Code));

    private static PathStepDto ToStepDto(PathStepInfo step) =>
        new(
            step.StepId,
            step.WeekNumber,
            step.WeekSequence,
            step.StepType.MapTo<PathStepTypeDto>(),
            step.Route.MapTo<StepRouteDto>(),
            step.Title,
            step.Status.MapTo<LearnerPathStepStatusDto>(),
            step.SkillCode,
            step.SkillName,
            step.Modality.MapTo<ModalityDto>(),
            step.TopicCode,
            step.TopicName,
            step.TargetCefr.Code,
            step.SizingTier.MapTo<SizingTierDto>(),
            step.PlannedItemCount,
            step.EstimatedMinutes,
            step.Score,
            step.ActualAccuracyPct,
            step.TargetAccuracyPct,
            step.CanStart,
            step.StartedAt,
            step.CompletedAt,
            step.RefLessonId,
            step.RefExamId);
}
