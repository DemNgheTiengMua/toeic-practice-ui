using Contracts;
using Microsoft.AspNetCore.Http.Features;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static class AdminLessonEndpoints
{
    public static void MapAdminLessonEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/lessons").RequireAuthorization(Roles.Admin);

        // GET /api/admin/lessons?certId={certId}&skillId={skillId}
        admin.MapGet("", async (int? certId, int? skillId, IAdminLessonService lessons, CancellationToken cancellationToken) =>
        {
            var result = await lessons.GetLessonsAsync(certId, skillId, cancellationToken);
            return Results.Ok(result);
        });

        // GET /api/admin/lessons/template
        admin.MapGet("/template", (IAdminLessonService lessons) =>
        {
            var template = lessons.GetTemplate();
            return Results.Ok(template);
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

        // POST /api/admin/lessons/import?certId={certId}&overwrite={bool}
        admin.MapPost("/import", async Task<IResult> (
            HttpRequest request,
            HttpContext http,
            IAdminLessonService lessons,
            CancellationToken cancellationToken) =>
        {
            var bodySize = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodySize is { IsReadOnly: false })
            {
                bodySize.MaxRequestBodySize = 100L * 1024 * 1024 + 1024 * 1024;
            }

            if (!request.HasFormContentType)
            {
                return Problem(StatusCodes.Status400BadRequest, "Expected multipart/form-data content.", ApiError.Codes.Validation);
            }

            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
            {
                return Problem(StatusCodes.Status400BadRequest, "Choose a file to import.", ApiError.Codes.Validation, "Choose a .json manifest or a .zip package to import.");
            }

            int certId;
            if (request.Query.TryGetValue("certId", out var qCert) && int.TryParse(qCert, out var parsedCert))
            {
                certId = parsedCert;
            }
            else if (!int.TryParse(form["certId"], out certId))
            {
                return Problem(StatusCodes.Status400BadRequest, "Choose a certificate.", ApiError.Codes.Validation, "Choose the certificate the lessons belong to.");
            }

            bool overwrite = false;
            if (request.Query.TryGetValue("overwrite", out var qOverwrite) && bool.TryParse(qOverwrite, out var parsedOverwrite))
            {
                overwrite = parsedOverwrite;
            }
            else if (bool.TryParse(form["overwrite"], out var formOverwrite))
            {
                overwrite = formOverwrite;
            }

            try
            {
                await using var stream = file.OpenReadStream();
                var result = await lessons.ImportLessonsAsync(certId, overwrite, stream, file.FileName, cancellationToken);
                return Results.Ok(result);
            }
            catch (AdminLessonException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        }).DisableAntiforgery();

        // GET /api/admin/lessons/{id:int}/export
        admin.MapGet("/{id:int}/export", async (int id, IAdminLessonService lessons, CancellationToken cancellationToken) =>
        {
            try
            {
                var manifest = await lessons.ExportLessonAsync(id, cancellationToken);
                return Results.Ok(manifest);
            }
            catch (AdminLessonException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });
    }
}
