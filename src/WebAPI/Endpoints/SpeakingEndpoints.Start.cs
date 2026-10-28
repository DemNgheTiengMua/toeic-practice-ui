using System.IO;
using Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class SpeakingEndpoints
{
    private static void MapStartRoutes(RouteGroupBuilder learner)
    {
        // POST /api/learner/speaking/{examId}/start
        learner.MapPost("/{examId:int}/start", async Task<IResult> (
            int examId,
            HttpContext http,
            ISpeakingPracticeService speakingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var attempt = await speakingService.StartAttemptAsync(CurrentUserId(http), examId, cancellationToken);
                return Results.Ok(new StartSpeakingAttemptResponse(
                    attempt.AttemptId,
                    attempt.ExamId,
                    attempt.Status.MapTo<AttemptStatusDto>(),
                    attempt.StartedAt,
                    attempt.ExpiresAt,
                    attempt.RemainingSeconds,
                    attempt.IsAutoSubmitted));
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.InsufficientCredits)
            {
                return Problem(StatusCodes.Status402PaymentRequired, "Insufficient credits", ApiError.Codes.InsufficientCredits, ex.Message);
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.ExamNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Exam not found", ApiError.Codes.ExamNotFound, ex.Message);
            }
        });
    }
}
