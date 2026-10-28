using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class WritingEndpoints
{
    private static void MapInstructionsRoutes(RouteGroupBuilder learner)
    {
        // GET /api/learner/writing/{examId}/instructions
        learner.MapGet("/{examId:int}/instructions", async Task<IResult> (
            int examId,
            HttpContext http,
            IWritingPracticeService writingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var view = await writingService.GetInstructionsAsync(CurrentUserId(http), examId, cancellationToken);
                return Results.Ok(MapInstructionsDto(view));
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.ExamNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Exam not found", ApiError.Codes.ExamNotFound, ex.Message);
            }
        });
    }
}
