using Contracts;
using Microsoft.AspNetCore.Http.Features;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminLessonEndpoints
{
    public static void MapAdminLessonEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/lessons").RequireAuthorization(Roles.Admin);
        MapCatalog(admin);
        MapAuthoring(admin);
        MapPackages(admin);
    }
}
