using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class WritingEndpoints
{
    private static void MapStartRoutes(RouteGroupBuilder learner)
    {
        // POST /api/learner/writing/{examId}/start
        learner.MapPost("/{examId:int}/start", async Task<IResult> (
            int examId,
            HttpContext http,
            IWritingPracticeService writingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var attempt = await writingService.StartAttemptAsync(CurrentUserId(http), examId, cancellationToken);
                return Results.Ok(new StartWritingAttemptResponse(
                    attempt.AttemptId,
                    attempt.ExamId,
                    attempt.Status.MapTo<AttemptStatusDto>(),
                    attempt.StartedAt,
                    attempt.ExpiresAt,
                    attempt.RemainingSeconds,
                    attempt.IsAutoSubmitted));
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.InsufficientCredits)
            {
                return Problem(StatusCodes.Status402PaymentRequired, "Insufficient credits", ApiError.Codes.InsufficientCredits, ex.Message);
            }
            catch (WritingPracticeException ex) when (ex.Code == WritingPracticeException.ExamNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Exam not found", ApiError.Codes.ExamNotFound, ex.Message);
            }
        });
    }
}
