using Contracts;
using Microsoft.AspNetCore.Http.Features;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminLessonEndpoints
{
    private static void MapCatalog(RouteGroupBuilder admin)
    {

        // GET /api/admin/lessons?certId={certId}&skillId={skillId}
        admin.MapGet("", async (int? certId, int? skillId, IAdminLessonService lessons, CancellationToken cancellationToken) =>
        {
            var result = await lessons.GetLessonsAsync(certId, skillId, cancellationToken);
            return Results.Ok(result);
        });

        // GET /api/admin/lessons/options?certId={certId}
        admin.MapGet("/options", async (int? certId, IAdminLessonService lessons, CancellationToken cancellationToken) =>
        {
            var options = await lessons.GetFormOptionsAsync(certId, cancellationToken);
            return Results.Ok(options);
        });

        // GET /api/admin/lessons/{id:int}
        admin.MapGet("/{id:int}", async (int id, IAdminLessonService lessons, CancellationToken cancellationToken) =>
        {
            var lesson = await lessons.GetLessonAsync(id, cancellationToken);
            if (lesson is null)
            {
                return Problem(StatusCodes.Status404NotFound, "Lesson not found", ApiError.Codes.LessonNotFound);
            }
            return Results.Ok(lesson);
        });
    }
}
