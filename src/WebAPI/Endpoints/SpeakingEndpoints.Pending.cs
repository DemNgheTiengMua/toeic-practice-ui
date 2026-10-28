using System.IO;
using Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class SpeakingEndpoints
{
    private static void MapPendingRoutes(RouteGroupBuilder learner)
    {
        // GET /api/learner/speaking/attempts/{attemptId}/pending
        learner.MapGet("/attempts/{attemptId:int}/pending", async Task<IResult> (
            int attemptId,
            HttpContext http,
            ISpeakingPracticeService speakingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var view = await speakingService.GetGradingPendingAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(MapPendingDto(view));
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
        });
    }
}
