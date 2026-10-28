using System.IO;
using Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class SpeakingEndpoints
{
    private static void MapInstructionsRoutes(RouteGroupBuilder learner)
    {
        // GET /api/learner/speaking/{examId}/instructions
        learner.MapGet("/{examId:int}/instructions", async Task<IResult> (
            int examId,
            HttpContext http,
            ISpeakingPracticeService speakingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var view = await speakingService.GetInstructionsAsync(CurrentUserId(http), examId, cancellationToken);
                return Results.Ok(MapInstructionsDto(view));
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.ExamNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Exam not found", ApiError.Codes.ExamNotFound, ex.Message);
            }
        });
    }
}
