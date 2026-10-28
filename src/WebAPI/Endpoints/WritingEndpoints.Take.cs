using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class WritingEndpoints
{
    private static void MapTakeRoutes(RouteGroupBuilder learner)
    {
        // GET /api/learner/writing/attempts/{attemptId}
        learner.MapGet("/attempts/{attemptId:int}", async Task<IResult> (
            int attemptId,
            [FromQuery] int? questionId,
            HttpContext http,
            IWritingPracticeService writingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var take = await writingService.GetAttemptTakeAsync(CurrentUserId(http), attemptId, questionId, cancellationToken);
                return Results.Ok(MapTakeDto(take));
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status409Conflict, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.AttemptExpired)
            {
                return Problem(StatusCodes.Status410Gone, "Attempt expired", ApiError.Codes.AttemptExpired, ex.Message);
            }
        });
    }
}
