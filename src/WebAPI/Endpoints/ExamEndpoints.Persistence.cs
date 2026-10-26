using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class ExamEndpoints
{
    private static void MapExamPersistenceEndpoints(RouteGroupBuilder learner)
    {
        learner.MapPost("/attempts/{attemptId:int}/sync", async Task<IResult> (
            int attemptId,
            [FromBody] SittingSyncRequestDto requestDto,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(http);
            try
            {
                var domainRequest = ToSittingSyncRequest(requestDto);

                var result = await mockExamService.SaveSittingSyncAsync(userId, attemptId, domainRequest, ct);

                return Results.Ok(ToSittingSyncDto(result));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptExpired)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt expired", ApiError.Codes.AttemptExpired, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == AttemptEngineException.WrongPhase)
            {
                return Problem(StatusCodes.Status400BadRequest, "Wrong phase", "wrong_phase", ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.PreflightRequired)
            {
                return Problem(StatusCodes.Status400BadRequest, "Preflight verification required", ApiError.Codes.PreflightRequired, ex.Message);
            }
        });

        learner.MapPost("/attempts/resume", async Task<IResult> (
            [FromBody] ResumeAttemptRequest request,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken ct) =>
        {
            var userId = CurrentUserId(http);
            try
            {
                var result = await mockExamService.ResumeAttemptByCodeAsync(request.AuthorizationCode, userId, ct);

                var dto = ToAttemptResumeDto(result);

                return Results.Ok(dto);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptExpired)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt expired", ApiError.Codes.AttemptExpired, ex.Message);
            }
        });
    }
}
