using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class ExamEndpoints
{
    private static void MapExamPreflightEndpoints(RouteGroupBuilder learner)
    {
        learner.MapGet("/attempts/{attemptId:int}/preflight", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var status = await mockExamService.GetPreflightStatusAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(ToPreflightStatusDto(status));
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

        learner.MapPost("/attempts/{attemptId:int}/preflight/verify", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var status = await mockExamService.VerifyPreflightAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(ToPreflightStatusDto(status));
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
