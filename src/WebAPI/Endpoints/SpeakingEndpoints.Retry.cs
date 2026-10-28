using System.IO;
using Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class SpeakingEndpoints
{
    private static void MapRetryRoutes(RouteGroupBuilder learner)
    {
        // POST /api/learner/speaking/attempts/{attemptId}/retry
        learner.MapPost("/attempts/{attemptId:int}/retry", async Task<IResult> (
            int attemptId,
            HttpContext http,
            ISpeakingPracticeService speakingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await speakingService.RetryGradingAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.NoContent();
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.RetryNotAllowed)
            {
                return Problem(StatusCodes.Status400BadRequest, "Retry not allowed", ApiError.Codes.Validation, ex.Message);
            }
        });
    }
}
