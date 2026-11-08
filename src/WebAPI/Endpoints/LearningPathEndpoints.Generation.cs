using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class LearningPathEndpoints
{
    private static void MapGeneration(RouteGroupBuilder learner)
    {

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
            // #236: the TemplateNotFound catch is gone with the templates. A path is synthesised from
            // current capability, so nothing can report a missing template any more.
            // #153: an Unrealistic goal is a refusal with reasons, not a server fault. 422 carries the
            // service's message — which names the hours needed and the hours available — to the screen.
            catch (LearningPathException ex) when (ex.Code == LearningPathException.GoalUnrealistic)
            {
                return Problem(StatusCodes.Status422UnprocessableEntity, "Goal does not fit the time available", ApiError.Codes.LearningPathGoalUnrealistic, ex.Message);
            }
            catch (LearningPathException ex) when (ex.Code is LearningPathException.StartingBandUnknown or LearningPathException.TargetBandMissing)
            {
                return Problem(StatusCodes.Status422UnprocessableEntity, "No goal to build a path from", ApiError.Codes.LearningPathBandUnknown, ex.Message);
            }
        });
    }
}
