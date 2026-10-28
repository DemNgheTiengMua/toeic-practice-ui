using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class WritingEndpoints
{
    private static void MapDraftRoutes(RouteGroupBuilder learner)
    {
        // POST /api/learner/writing/attempts/{attemptId}/draft
        learner.MapPost("/attempts/{attemptId:int}/draft", async Task<IResult> (
            int attemptId,
            [FromBody] SaveWritingDraftRequest request,
            HttpContext http,
            IWritingPracticeService writingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await writingService.SaveDraftAsync(CurrentUserId(http), attemptId, request.QuestionId, request.ResponseText, cancellationToken);
                return Results.Ok(new SaveWritingDraftResponse(
                    result.FreeResponseId,
                    result.QuestionId,
                    result.SavedAt,
                    result.WordCount));
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
