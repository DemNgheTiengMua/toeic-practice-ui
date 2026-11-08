using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class LearningPathEndpoints
{
    /// <summary>
    /// #232: one step, by its own id. A step is no longer addressed through a module, so this replaces
    /// the module-detail endpoint that existed only to render a template's module.
    /// </summary>
    private static void MapModules(RouteGroupBuilder learner)
    {

        // GET /api/learner/learning-path/steps/{stepId:int}: one step's coordinate and state
        learner.MapGet("/steps/{stepId:int}", async Task<IResult> (
            int stepId,
            HttpContext http,
            ILearningPathService service,
            CancellationToken cancellationToken) =>
        {
            var step = await service.GetStepAsync(CurrentUserId(http), stepId, cancellationToken);
            return step is null
                ? Problem(StatusCodes.Status404NotFound, "Step not found", ApiError.Codes.LearningPathStepNotFound, $"Step {stepId} is not on this learner's active path.")
                : Results.Ok(ToStepDto(step));
        });

        // POST /api/learner/learning-path/steps/{stepId:int}/start: launch the step on its own route
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
                    result.StepType.MapTo<PathStepTypeDto>(),
                    result.Route.MapTo<StepRouteDto>(),
                    result.RedirectUrl,
                    result.SessionId,
                    result.AttemptId,
                    result.SubstitutedFromTopicId));
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
