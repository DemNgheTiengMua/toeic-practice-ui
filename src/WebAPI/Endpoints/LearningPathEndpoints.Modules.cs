using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class LearningPathEndpoints
{
    private static void MapModules(RouteGroupBuilder learner)
    {

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
    }
}
