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

        // #286 / ADR-0013: the revision the live stream compares. Same capability as the dashboard it
        // describes - a caller who may not read the figures may not watch them change either - and it sits
        // under the existing group so the gate is the group's, not a second one to keep in step.
        admin.MapGet("/dashboard/revision", async (IDashboardRevisionService revision, CancellationToken cancellationToken) =>
        {
            var current = await revision.GetRevisionAsync(cancellationToken);
            return Results.Ok(current);
        });
    }
}
