using System.Security.Claims;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // ---- Admin endpoints ----
        var admin = app.MapGroup("/api/admin").RequireAuthorization(Roles.Admin);
        MapSummary(admin);
        MapDashboard(admin);
        MapPackages(admin);
        MapOrders(admin);
        MapUsers(admin);
        MapScoreConversion(admin);
        MapExams(admin);
        MapQuestions(admin);
        MapQuickIngest(admin);
        MapStudio(admin);
        MapCertificates(admin);
        MapMedia(app);
    }
}
