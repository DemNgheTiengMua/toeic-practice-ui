using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static class LearningPathEndpoints
{
    public static void MapLearningPathEndpoints(this IEndpointRouteBuilder app)
    {
        var learner = app.MapGroup("/api/learner/learning-path").RequireAuthorization(Roles.Learner);

        // GET /api/learner/learning-path: active path overview with modules and progress
        learner.MapGet("", async (
            HttpContext http,
            ILearningPathService service,
            CancellationToken cancellationToken) =>
        {
            var view = await service.GetActivePathAsync(CurrentUserId(http), cancellationToken);
            return Results.Ok(ToSummaryDto(view));
        });

        // POST /api/learner/learning-path/generate: generate path from placement/target
        learner.MapPost("/generate", async Task<IResult> (
            [FromBody] GenerateLearningPathRequest? request,
            HttpContext http,
            ILearningPathService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var view = await service.GeneratePathAsync(CurrentUserId(http), request?.TargetBandCode, cancellationToken, request?.ArchiveExisting ?? false);
                return Results.Ok(ToSummaryDto(view));
            }
            catch (LearningPathException ex) when (ex.Code == LearningPathException.PathAlreadyActive)
            {
                return Problem(StatusCodes.Status409Conflict, "Learning path already active", ApiError.Codes.LearningPathAlreadyActive, ex.Message);
            }
            catch (LearningPathException ex) when (ex.Code == LearningPathException.TemplateNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Path template not found", ApiError.Codes.LearningPathTemplateNotFound, ex.Message);
            }
        });

        // GET /api/learner/learning-path/modules/{moduleId:int}: module detail with steps
        learner.MapGet("/modules/{moduleId:int}", async Task<IResult> (
            int moduleId,
            HttpContext http,
            ILearningPathService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var detail = await service.GetModuleDetailAsync(CurrentUserId(http), moduleId, cancellationToken);
                return Results.Ok(ToDetailDto(detail));
            }
            catch (LearningPathException ex) when (ex.Code == LearningPathException.PathNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Active learning path not found", ApiError.Codes.LearningPathNotFound, ex.Message);
            }
            catch (LearningPathException ex) when (ex.Code == LearningPathException.ModuleNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Module not found", ApiError.Codes.LearningPathModuleNotFound, ex.Message);
            }
        });

        // POST /api/learner/learning-path/steps/{stepId:int}/start: launch the step
        learner.MapPost("/steps/{stepId:int}/start", async Task<IResult> (
            int stepId,
            HttpContext http,
            ILearningPathService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.StartStepAsync(CurrentUserId(http), stepId, cancellationToken);
                return Results.Ok(new StartStepResultDto(
                    result.StepId,
                    result.ItemType.MapTo<PathModuleItemTypeDto>(),
                    result.RedirectUrl,
                    result.SessionId,
                    result.AttemptId));
            }
            catch (LearningPathException ex) when (ex.Code == LearningPathException.StepNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Step not found", ApiError.Codes.LearningPathStepNotFound, ex.Message);
            }
            catch (LearningPathException ex) when (ex.Code == LearningPathException.StepLocked)
            {
                return Problem(StatusCodes.Status400BadRequest, "Step is locked", ApiError.Codes.LearningPathStepLocked, ex.Message);
            }
            catch (LearningPathException ex) when (ex.Code == LearningPathException.NoQuestionsAvailable)
            {
                return Problem(StatusCodes.Status404NotFound, "No questions available", ApiError.Codes.PracticeNoQuestionsAvailable, ex.Message);
            }
        });

        // POST /api/learner/learning-path/reset: reset current path
        learner.MapPost("/reset", async (
            HttpContext http,
            ILearningPathService service,
            CancellationToken cancellationToken) =>
        {
            var success = await service.ResetActivePathAsync(CurrentUserId(http), cancellationToken);
            return Results.Ok(new ResetLearningPathResultDto(success));
        });

        // POST /api/learner/learning-path/upgrade: unlock premium path
        learner.MapPost("/upgrade", async Task<IResult> (
            HttpContext http,
            ILearningPathService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var summary = await service.UpgradePathAsync(CurrentUserId(http), cancellationToken);
                return Results.Ok(ToSummaryDto(summary));
            }
            catch (LearningPathException ex) when (ex.Code == LearningPathException.PathNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Active learning path not found", ApiError.Codes.LearningPathNotFound, ex.Message);
            }
            catch (LearningPathException ex) when (ex.Code == LearningPathException.InsufficientCredits)
            {
                return Problem(StatusCodes.Status400BadRequest, "Insufficient credits", ApiError.Codes.InsufficientCredits, ex.Message);
            }
            catch (LearningPathException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Learning path error", ex.Code, ex.Message);
            }
        });
    }

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
