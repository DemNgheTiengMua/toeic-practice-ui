using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class LearningPathEndpoints
{
    private static void MapLifecycle(RouteGroupBuilder learner)
    {

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
}
