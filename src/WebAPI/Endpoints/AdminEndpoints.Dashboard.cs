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

        // #351: `month` is any day of the month to report on; its day is ignored. Absent, it means the
        // month the clock is in, so every caller that predates the parameter reads exactly what it did.
        admin.MapGet("/dashboard", async (
            DateOnly? month,
            IAdminDashboardService dashboard,
            CancellationToken cancellationToken) =>
        {
            var metrics = await dashboard.GetDashboardMetricsAsync(month, cancellationToken);
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
