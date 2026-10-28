using System.Security.Claims;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapDashboard(RouteGroupBuilder admin)
    {

        admin.MapGet("/dashboard", async (IAdminDashboardService dashboard, CancellationToken cancellationToken) =>
        {
            var metrics = await dashboard.GetDashboardMetricsAsync(cancellationToken);
            return Results.Ok(metrics);
        });
    }
}
