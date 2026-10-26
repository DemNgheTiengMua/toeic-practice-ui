using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class ExamEndpoints
{
    private static void MapExamAnswersEndpoints(RouteGroupBuilder learner)
    {
        learner.MapPost("/attempts/{attemptId:int}/draft", async Task<IResult> (
            int attemptId,
            SaveAttemptDraftRequest request,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await mockExamService.SaveDraftAsync(
                    CurrentUserId(http),
                    attemptId,
                    request.QuestionId,
                    request.ResponseText,
                    cancellationToken);
                return Results.Ok(ToSaveAttemptDraftResponse(result));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.QuestionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Draft not found", ApiError.Codes.QuestionNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptExpired)
            {
                return Problem(StatusCodes.Status400BadRequest, "Exam deadline expired", ApiError.Codes.AttemptExpired, ex.Message);
            }
            catch (MockExamException ex) when (
                ex.Code == MockExamException.AttemptNotActive ||
                ex.Code == AttemptEngineException.WrongPhase)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.PreflightRequired)
            {
                return Problem(StatusCodes.Status400BadRequest, "Preflight verification required", ApiError.Codes.PreflightRequired, ex.Message);
            }
        });

        learner.MapPost("/attempts/{attemptId:int}/answers/{questionId:int}", async Task<IResult> (
            int attemptId,
            int questionId,
            SaveAnswerRequest request,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var res = await mockExamService.SaveAnswerAsync(
                    CurrentUserId(http),
                    attemptId,
                    questionId,
                    request.SelectedOptionId,
                    request.IsFlagged,
                    cancellationToken,
                    request.ResponseText);
                return Results.Ok(ToSaveAnswerResultDto(res));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptExpired)
            {
                return Problem(StatusCodes.Status400BadRequest, "Exam deadline expired", ApiError.Codes.AttemptExpired, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.QuestionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Question not found", ApiError.Codes.QuestionNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.InvalidOption)
            {
                return Problem(StatusCodes.Status400BadRequest, "Invalid option", ApiError.Codes.PracticeInvalidOption, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == AttemptEngineException.WrongPhase)
            {
                return Problem(StatusCodes.Status400BadRequest, "Question not available yet", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.PreflightRequired)
            {
                return Problem(StatusCodes.Status400BadRequest, "Preflight verification required", ApiError.Codes.PreflightRequired, ex.Message);
            }
        });
    }
}
