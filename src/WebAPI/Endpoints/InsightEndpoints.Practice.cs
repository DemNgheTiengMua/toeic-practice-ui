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
    private static void MapPractice(RouteGroupBuilder learner)
    {

        // GET /api/learner/insights/practice/{sessionId}
        learner.MapGet("/practice/{sessionId:int}", async Task<IResult> (
            int sessionId,
            HttpContext http,
            IAiInsightService insightService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var view = await insightService.GetOrCreatePracticeInsightAsync(CurrentUserId(http), sessionId, cancellationToken);
                return Results.Ok(ToDto(view));
            }
            catch (AiInsightException ex) when (ex.Code == ApiError.Codes.PracticeSessionNotFound)
            {
                return Problem(StatusCodes.Status404NotFound, "Practice session not found", ApiError.Codes.PracticeSessionNotFound, ex.Message);
            }
            catch (AiInsightException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Insight error", ex.Code, ex.Message);
            }
        });

        // POST /api/learner/insights/practice/{sessionId}/retry
        learner.MapPost("/practice/{sessionId:int}/retry", async Task<IResult> (
            int sessionId,
            HttpContext http,
            IAiInsightService insightService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await insightService.RetryPracticeInsightAsync(CurrentUserId(http), sessionId, cancellationToken);
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
