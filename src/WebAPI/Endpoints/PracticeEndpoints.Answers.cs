using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class PracticeEndpoints
{
    private static void MapAnswers(RouteGroupBuilder practice)
    {

        practice.MapPost("/sessions/{sessionId:int}/questions/{questionId:int}/hint", async Task<IResult> (
            int sessionId,
            int questionId,
            HttpContext http,
            IPracticeService practiceService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var hint = await practiceService.RevealHintAsync(CurrentUserId(http), sessionId, questionId, cancellationToken);
                return Results.Ok(new RevealHintResponse(hint, true));
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.SessionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Session not found", ApiError.Codes.PracticeSessionNotFound, ex.Message);
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.QuestionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Question not found", ApiError.Codes.PracticeQuestionNotFound, ex.Message);
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.SessionAlreadyFinished)
            {
                return Problem(StatusCodes.Status400BadRequest, "Session already finished", ApiError.Codes.PracticeSessionAlreadyFinished, ex.Message);
            }
        });

        practice.MapPost("/sessions/{sessionId:int}/questions/{questionId:int}/answer", async Task<IResult> (
            int sessionId,
            int questionId,
            SubmitPracticeAnswerRequest request,
            HttpContext http,
            IPracticeService practiceService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await practiceService.SubmitAnswerAsync(
                    CurrentUserId(http),
                    sessionId,
                    questionId,
                    new SubmitPracticeAnswerInput(request.SelectedOptionId, request.ResponseText, request.SelectedOptionIdsJson, request.SelectedOptionIds),
                    cancellationToken);
                return Results.Ok(new GradedAnswerDto(
                    result.IsCorrect,
                    result.SelectedOptionId,
                    result.CorrectOptionId,
                    result.Explanation,
                    result.Transcript,
                    result.IsSessionFinished,
                    result.NextQuestionDisplayOrder,
                    result.IsSaved,
                    result.AcceptedAnswersJson,
                    result.CorrectOptionIds,
                    result.ResponseText,
                    result.SelectedOptionIdsJson));
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.SessionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Session not found", ApiError.Codes.PracticeSessionNotFound, ex.Message);
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.QuestionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Question not found", ApiError.Codes.PracticeQuestionNotFound, ex.Message);
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.InvalidOption)
            {
                return Problem(StatusCodes.Status400BadRequest, "Invalid option", ApiError.Codes.PracticeInvalidOption, ex.Message);
            }
            catch (PracticeException ex) when (ex.Code == PracticeException.SessionAlreadyFinished)
            {
                return Problem(StatusCodes.Status400BadRequest, "Session already finished", ApiError.Codes.PracticeSessionAlreadyFinished, ex.Message);
            }
        });
    }
}
