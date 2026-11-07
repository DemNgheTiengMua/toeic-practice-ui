using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class ExamEndpoints
{
    private static void MapExamResultsEndpoints(RouteGroupBuilder learner)
    {
        learner.MapGet("/attempts/{attemptId:int}/result", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var r = await mockExamService.GetAttemptResultAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(ToAttemptResultDto(r));
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

        learner.MapGet("/exams/history", async (
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            var history = await mockExamService.GetExamHistoryAsync(CurrentUserId(http), cancellationToken);
            return Results.Ok(ToExamHistoryDto(history));
        });

        learner.MapGet("/attempts/{attemptId:int}/review", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IMockExamService mockExamService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var review = await mockExamService.GetAttemptReviewAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(ToAttemptReviewDto(review));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFinished)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not finished", ApiError.Codes.AttemptNotFinished, ex.Message);
            }
        });

        // #222: the projection READ. It writes nothing - the sync below is the only writer on this path,
        // and this endpoint has no route to the skill-state writer at all.
        learner.MapGet("/attempts/{attemptId:int}/projection", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IMockResultSyncService mockResults,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var projection = await mockResults.GetResultAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(ToMockResultProjectionDto(projection));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFinished)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not finished", ApiError.Codes.AttemptNotFinished, ex.Message);
            }
        });

        // #222: the explicit sync. A POST because it writes, and it refuses when there is nothing
        // legitimate to write (historical attempt, cannot-judge projection, or an already-matching state).
        learner.MapPost("/attempts/{attemptId:int}/projection/sync", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IMockResultSyncService mockResults,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var standing = await mockResults.SyncAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(ToMockSyncResultDto(standing));
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.NothingToSync)
            {
                return Problem(StatusCodes.Status409Conflict, "Nothing to sync", ApiError.Codes.NothingToSync, ex.Message);
            }
            catch (MockExamException ex) when (ex.Code == MockExamException.AttemptNotFinished)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not finished", ApiError.Codes.AttemptNotFinished, ex.Message);
            }
        });
    }
}
