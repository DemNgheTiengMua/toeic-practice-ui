using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class ExamEndpoints
{
    private static void MapExamTransitionsEndpoints(RouteGroupBuilder learner)
    {
        learner.MapPost("/attempts/{attemptId:int}/transition/skill", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var res = await mockExamService.TransitionToNextSkillAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(ToAttemptTransitionResponse(res));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (MockExamException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Transition failed", ApiError.Codes.Validation, ex.Message);
            }
        });

        learner.MapPost("/attempts/{attemptId:int}/transition/unit", async Task<IResult> (
            int attemptId,
            [FromBody] TransitionUnitRequest request,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var res = await mockExamService.TransitionUnitAsync(CurrentUserId(http), attemptId, request.TargetUnitNumber, cancellationToken);
                return Results.Ok(ToAttemptTransitionResponse(res));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (MockExamException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Transition failed", ApiError.Codes.Validation, ex.Message);
            }
        });

        learner.MapPost("/attempts/{attemptId:int}/transition-group", async Task<IResult> (
            int attemptId,
            [FromBody] TransitionGroupRequest request,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var res = await mockExamService.TransitionGroupAsync(CurrentUserId(http), attemptId, request.TargetGroupId, cancellationToken);
                return Results.Ok(ToAttemptTransitionResponse(res));
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
                return Problem(StatusCodes.Status404NotFound, "Question group not found", ApiError.Codes.QuestionNotFound, ex.Message);
            }
            catch (MockExamException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Transition failed", ApiError.Codes.Validation, ex.Message);
            }
        });

        learner.MapPost("/attempts/{attemptId:int}/transition/group", async Task<IResult> (
            int attemptId,
            [FromBody] TransitionGroupRequest request,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var res = await mockExamService.TransitionGroupAsync(CurrentUserId(http), attemptId, request.TargetGroupId, cancellationToken);
                return Results.Ok(ToAttemptTransitionResponse(res));
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
                return Problem(StatusCodes.Status404NotFound, "Question group not found", ApiError.Codes.QuestionNotFound, ex.Message);
            }
            catch (MockExamException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Transition failed", ApiError.Codes.Validation, ex.Message);
            }
        });

        learner.MapPost("/attempts/{attemptId:int}/audio-review/start", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var res = await mockExamService.CompleteAudioAndStartReviewAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(ToAttemptTransitionResponse(res));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (MockExamException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Audio review transition failed", ApiError.Codes.Validation, ex.Message);
            }
        });
    }
}
