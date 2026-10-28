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
    private static void MapRetries(RouteGroupBuilder learner)
    {

        // POST /api/learner/insights/{insightId}/retry
        learner.MapPost("/{insightId:int}/retry", async Task<IResult> (
            int insightId,
            HttpContext http,
            IAiInsightService insightService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await insightService.RetryInsightAsync(CurrentUserId(http), insightId, cancellationToken);
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
