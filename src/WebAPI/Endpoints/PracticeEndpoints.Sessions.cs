using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class PracticeEndpoints
{
    private static void MapSessions(RouteGroupBuilder practice)
    {

        practice.MapPost("/sessions", async Task<IResult> (
            StartPracticeRequest request,
            HttpContext http,
            IPracticeService practiceService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var input = StartPracticeSessionInput.FromCodes(
                    request.SectionId,
                    request.QuestionCount,
                    request.QuestionSource,
                    request.FeedbackMode);

                var session = await practiceService.StartSessionAsync(CurrentUserId(http), input, cancellationToken);
                return Results.Ok(new PracticeSessionDto(
                    session.SessionId,
                    session.SectionId,
                    session.SectionCode,
                    session.SectionName,
                    session.Modality.MapTo<ModalityDto>(),
                    session.QuestionCount,
                    session.Status.MapTo<PracticeStatusDto>(),
                    session.CorrectCount,
                    session.QuestionSource.MapTo<QuestionSourceDto>(),
                    session.FeedbackMode.MapTo<FeedbackModeDto>(),
                    session.StartedAt,
                    session.FinishedAt));
            }
            catch (ValidationException ex)
            {
                return ValidationProblem(ex);
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.ModalityNotAllowed)
            {
                return Problem(StatusCodes.Status400BadRequest, "Modality not allowed for free practice", ApiError.Codes.PracticeModalityNotAllowed, ex.Message);
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.SectionUnavailable)
            {
                return Problem(StatusCodes.Status404NotFound, "Section unavailable", ApiError.Codes.PracticeSectionUnavailable, ex.Message);
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.NoQuestionsAvailable)
            {
                return Problem(StatusCodes.Status404NotFound, "No questions available", ApiError.Codes.PracticeNoQuestionsAvailable, ex.Message);
            }
        });

        practice.MapGet("/sessions/{sessionId:int}", async Task<IResult> (
            int sessionId,
            HttpContext http,
            IPracticeService practiceService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var session = await practiceService.GetSessionInfoAsync(CurrentUserId(http), sessionId, cancellationToken);
                return Results.Ok(new PracticeSessionDto(
                    session.SessionId,
                    session.SectionId,
                    session.SectionCode,
                    session.SectionName,
                    session.Modality.MapTo<ModalityDto>(),
                    session.QuestionCount,
                    session.Status.MapTo<PracticeStatusDto>(),
                    session.CorrectCount,
                    session.QuestionSource.MapTo<QuestionSourceDto>(),
                    session.FeedbackMode.MapTo<FeedbackModeDto>(),
                    session.StartedAt,
                    session.FinishedAt));
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.SessionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Session not found", ApiError.Codes.PracticeSessionNotFound, ex.Message);
            }
        });
    }
}
