using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class ExamEndpoints
{
    private static void MapExamSubmissionEndpoints(RouteGroupBuilder learner)
    {
        learner.MapPost("/attempts/{attemptId:int}/submit", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var res = await mockExamService.SubmitAttemptAsync(CurrentUserId(http), attemptId, isAutoSubmit: false, cancellationToken);
                return Results.Ok(ToSubmitAttemptResponse(res));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
        });

        learner.MapPost("/attempts/{attemptId:int}/submit-with-drafts", async Task<IResult> (
            int attemptId,
            [FromBody] SubmitAttemptRequest request,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var finalDrafts = ToFinalDrafts(request);
                var res = await mockExamService.SubmitAttemptAsync(
                    CurrentUserId(http),
                    attemptId,
                    isAutoSubmit: false,
                    cancellationToken,
                    finalDrafts);
                return Results.Ok(ToSubmitAttemptResponse(res));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.QuestionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Question not found", ApiError.Codes.QuestionNotFound, ex.Message);
            }
        });

        learner.MapPost("/attempts/{attemptId:int}/cancel", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await mockExamService.CancelAttemptAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.NoContent();
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
        });
    }
}
