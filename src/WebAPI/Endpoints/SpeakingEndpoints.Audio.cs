using System.IO;
using Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class SpeakingEndpoints
{
    private static void MapAudioRoutes(RouteGroupBuilder learner)
    {
        // GET /api/learner/speaking/attempts/{attemptId}/questions/{questionId}/audio (ownership-checked audio endpoint)
        learner.MapGet("/attempts/{attemptId:int}/questions/{questionId:int}/audio", async Task<IResult> (
            int attemptId,
            int questionId,
            HttpContext http,
            ISpeakingPracticeService speakingService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var fileInfo = await speakingService.GetAudioFileAsync(CurrentUserId(http), attemptId, questionId, cancellationToken);
                if (fileInfo is null)
                {
                    return Problem(StatusCodes.Status404NotFound, "Audio file not found", ApiError.Codes.AttemptNotFound, "The audio file could not be found.");
                }

                // #365: no file name here — Results.Stream turns a file name into
                // "Content-Disposition: attachment", which makes the browser download the take instead of
                // playing it in the result page's <audio>. ContentType already maps every stored take
                // extension to a playable audio/* type (FileStore.GetContentTypeFromFileName).
                return Results.Stream(fileInfo.Stream, fileInfo.ContentType, enableRangeProcessing: true);
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.Forbidden)
            {
                return Problem(StatusCodes.Status403Forbidden, "Forbidden", ApiError.Codes.Forbidden, ex.Message);
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.AttemptNotFound || ex.Code == SpeakingPracticeException.AudioNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Audio not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
        });
    }
}
