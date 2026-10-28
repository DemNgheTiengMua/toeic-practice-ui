using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class WritingEndpoints
{
    private static void MapSubmitRoutes(RouteGroupBuilder learner)
    {
        // POST /api/learner/writing/attempts/{attemptId}/submit
        learner.MapPost("/attempts/{attemptId:int}/submit", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IWritingPracticeService writingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await writingService.SubmitAttemptAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(new SubmitWritingResponse(
                    result.AttemptId,
                    result.Status.MapTo<AttemptStatusDto>(),
                    result.SubmittedAt));
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.InsufficientCredits)
            {
                return Problem(StatusCodes.Status402PaymentRequired, "Insufficient credits", ApiError.Codes.InsufficientCredits, ex.Message);
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status409Conflict, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
        });
    }
}
