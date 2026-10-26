using Contracts;
using Microsoft.AspNetCore.Mvc;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class ExamEndpoints
{
    private static void MapExamSittingEndpoints(RouteGroupBuilder learner)
    {
        learner.MapGet("/attempts/{attemptId:int}", async Task<IResult> (
            int attemptId,
            [FromQuery] int? q,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var take = await mockExamService.GetAttemptTakeAsync(CurrentUserId(http), attemptId, q, cancellationToken);
                return Results.Ok(ToAttemptTakeDto(take));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotActive)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not active", ApiError.Codes.AttemptNotActive, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.PreflightRequired)
            {
                return Problem(StatusCodes.Status400BadRequest, "Preflight verification required", ApiError.Codes.PreflightRequired, ex.Message);
            }
        });
    }
}
