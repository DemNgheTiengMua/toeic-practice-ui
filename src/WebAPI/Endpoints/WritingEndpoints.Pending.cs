using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class WritingEndpoints
{
    private static void MapPendingRoutes(RouteGroupBuilder learner)
    {
        // GET /api/learner/writing/attempts/{attemptId}/pending
        learner.MapGet("/attempts/{attemptId:int}/pending", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IWritingPracticeService writingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var view = await writingService.GetGradingPendingAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(MapPendingDto(view));
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
        });
    }
}
