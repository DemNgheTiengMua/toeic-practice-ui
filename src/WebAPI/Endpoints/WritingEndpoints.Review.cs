using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class WritingEndpoints
{
    private static void MapReviewRoutes(RouteGroupBuilder learner)
    {
        // GET /api/learner/writing/attempts/{attemptId}/review
        learner.MapGet("/attempts/{attemptId:int}/review", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IWritingPracticeService writingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var review = await writingService.GetAttemptReviewAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(MapReviewDto(review));
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.AttemptNotFinished)
            {
                return Problem(StatusCodes.Status409Conflict, "Attempt not graded yet", ApiError.Codes.AttemptNotFinished, ex.Message);
            }
        });
    }
}
