using Contracts;
using Microsoft.AspNetCore.Http.Features;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminLessonEndpoints
{
    private static void MapPackages(RouteGroupBuilder admin)
    {

        // GET /api/admin/lessons/template
        admin.MapGet("/template", (IAdminLessonService lessons) =>
        {
            var template = lessons.GetTemplate();
            return Results.Ok(template);
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
