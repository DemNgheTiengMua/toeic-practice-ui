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
    private static void MapDashboard(RouteGroupBuilder learner)
    {

        // GET /api/learner/insights/dashboard
        learner.MapGet("/dashboard", async (
            int? certId,
            HttpContext http,
            IAiInsightService insightService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var view = await insightService.GetOrCreateDashboardInsightAsync(CurrentUserId(http), certId, cancellationToken);
                return Results.Ok(ToDto(view));
            }
            catch (AiInsightException ex) when (ex.Code == ApiError.Codes.CertificateUnavailable)
            {
                return Problem(StatusCodes.Status404NotFound, "Certificate not found", ApiError.Codes.CertificateUnavailable, ex.Message);
            }
            catch (AiInsightException ex)
            {
                return Problem(StatusCodes.Status400BadRequest, "Insight error", ex.Code, ex.Message);
            }
        });

        // POST /api/learner/insights/dashboard/retry
        learner.MapPost("/dashboard/retry", async (
            int? certId,
            HttpContext http,
            IAiInsightService insightService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await insightService.RetryDashboardInsightAsync(CurrentUserId(http), certId, cancellationToken);
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
