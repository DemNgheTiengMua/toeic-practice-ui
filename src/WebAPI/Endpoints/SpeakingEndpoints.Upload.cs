using System.IO;
using Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class SpeakingEndpoints
{
    private static void MapUploadRoutes(RouteGroupBuilder learner)
    {
        // POST /api/learner/speaking/attempts/{attemptId}/questions/{questionId}/audio
        learner.MapPost("/attempts/{attemptId:int}/questions/{questionId:int}/audio", async Task<IResult> (
            int attemptId,
            int questionId,
            HttpRequest request,
            HttpContext http,
            ISpeakingPracticeService speakingService,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
            {
                return Problem(StatusCodes.Status400BadRequest, "Expected multipart/form-data content.", ApiError.Codes.Validation);
            }

            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("audioFile");
            if (file is null || file.Length == 0)
            {
                return Problem(StatusCodes.Status400BadRequest, "Audio file is required.", ApiError.Codes.Validation, "Audio file cannot be empty.");
            }

            int? durationMs = null;
            if (form.TryGetValue("durationMs", out var durationValue) && int.TryParse(durationValue, out int parsedDuration))
            {
                durationMs = parsedDuration;
            }

            try
            {
                using var stream = file.OpenReadStream();
                var result = await speakingService.SaveAudioAsync(
                    CurrentUserId(http),
                    attemptId,
                    questionId,
                    stream,
                    file.ContentType,
                    file.FileName,
                    durationMs,
                    cancellationToken);

                return Results.Ok(new UploadSpeakingAudioResponse(
                    result.FreeResponseId,
                    result.QuestionId,
                    result.AudioPath,
                    result.DurationMs,
                    result.SavedAt));
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.InvalidAudio)
            {
                return Problem(StatusCodes.Status400BadRequest, "Invalid audio file", ApiError.Codes.Validation, ex.Message);
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status409Conflict, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (SpeakingPracticeException ex) when (ex.Code == SpeakingPracticeException.AttemptExpired)
            {
                return Problem(StatusCodes.Status410Gone, "Attempt expired", ApiError.Codes.AttemptExpired, ex.Message);
            }
        }).DisableAntiforgery();
    }
}
