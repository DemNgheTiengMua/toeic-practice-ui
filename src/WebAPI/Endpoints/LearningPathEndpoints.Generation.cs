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
            catch (LearningPathException ex) when (ex.Code == LearningPathException.TemplateNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Path template not found", ApiError.Codes.LearningPathTemplateNotFound, ex.Message);
            }
        });
    }
}
