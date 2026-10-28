using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class InsightEndpoints
{
    private static void MapExams(RouteGroupBuilder learner)
    {

        // GET /api/learner/insights/exam/{attemptId}
        learner.MapGet("/exam/{attemptId:int}", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IAiInsightService insightService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var view = await insightService.GetOrCreateExamInsightAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.Ok(ToDto(view));
            }
            catch (AiInsightException ex) when (ex.Code == ApiError.Codes.AttemptNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Attempt not found", ApiError.Codes.AttemptNotFound, ex.Message);
            }
            catch (AiInsightException ex) when (ex.Code == ApiError.Codes.AttemptNotFinished)
            {
                return Problem(StatusCodes.Status400BadRequest, "Attempt not finished", ApiError.Codes.AttemptNotFinished, ex.Message);
            }
            catch (AiInsightException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Insight error", ex.Code, ex.Message);
            }
        });

        // POST /api/learner/insights/exam/{attemptId}/retry
        learner.MapPost("/exam/{attemptId:int}/retry", async Task<IResult> (
            int attemptId,
            HttpContext http,
            IAiInsightService insightService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await insightService.RetryExamInsightAsync(CurrentUserId(http), attemptId, cancellationToken);
                return Results.NoContent();
            }
            catch (AiInsightException ex) when (ex.Code == ApiError.Codes.InsightNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Insight not found", ApiError.Codes.InsightNotFound, ex.Message);
            }
            catch (AiInsightException ex) when (ex.Code == ApiError.Codes.InsightNotFailed)
            {
                return Problem(StatusCodes.Status400BadRequest, "Retry not allowed", ApiError.Codes.Validation, ex.Message);
            }
            catch (AiInsightException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Insight error", ex.Code, ex.Message);
            }
        });
    }
}
