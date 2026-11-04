using Contracts;
using Microsoft.AspNetCore.Http.Features;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminLessonEndpoints
{
    public static void MapAdminLessonEndpoints(this IEndpointRouteBuilder app)
    {
        // D16: authoring lessons is content authoring.
        var admin = app.MapGroup("/api/admin/lessons").RequireAuthorization(Permissions.ContentAuthor);
        MapCatalog(admin);
        MapAuthoring(admin);
        MapPackages(admin);
    }
}
