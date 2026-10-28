using Contracts;
using Microsoft.AspNetCore.Http.Features;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminLessonEndpoints
{
    private static void MapAuthoring(RouteGroupBuilder admin)
    {

        // POST /api/admin/lessons
        admin.MapPost("", async (AdminLessonUpsertDto request, IAdminLessonService lessons, CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await lessons.CreateLessonAsync(request, cancellationToken);
                return Results.Created($"/api/admin/lessons/{created.LessonId}", created);
            }
            catch (AdminLessonException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        // PUT /api/admin/lessons/{id:int}
        admin.MapPut("/{id:int}", async (int id, AdminLessonUpsertDto request, IAdminLessonService lessons, CancellationToken cancellationToken) =>
        {
            try
            {
                var updated = await lessons.UpdateLessonAsync(id, request, cancellationToken);
                return Results.Ok(updated);
            }
            catch (AdminLessonException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        // DELETE /api/admin/lessons/{id:int}
        admin.MapDelete("/{id:int}", async (int id, IAdminLessonService lessons, CancellationToken cancellationToken) =>
        {
            try
            {
                await lessons.DeleteLessonAsync(id, cancellationToken);
                return Results.NoContent();
            }
            catch (AdminLessonException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });
    }
}
