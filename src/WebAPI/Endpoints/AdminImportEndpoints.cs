using Contracts;
using Microsoft.AspNetCore.Http.Features;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

/// <summary>Question batch import (ADR-0007) and the endpoint that serves imported media.</summary>
public static class AdminImportEndpoints
{
    public static void MapAdminImportEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/imports").RequireAuthorization(Roles.Admin);

        admin.MapGet("", async (int? certId, IQuestionImportService imports, CancellationToken cancellationToken) =>
            Results.Ok(await imports.GetBatchesAsync(certId, cancellationToken)));

        admin.MapGet("/{batchId:int}", async (int batchId, IQuestionImportService imports, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await imports.GetBatchAsync(batchId, cancellationToken));
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        // POST /api/admin/imports (multipart: certId, file). Bearer-authenticated, so no anti-forgery
        // token; the MVC site checks its own before forwarding (ADR-0002).
        admin.MapPost("", async Task<IResult> (
            HttpRequest request,
            HttpContext http,
            IQuestionImportService imports,
            CancellationToken cancellationToken) =>
        {
            // Kestrel's default 30 MB body cap is below the package limit; raise it for this request only.
            var bodySize = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodySize is { IsReadOnly: false })
            {
                bodySize.MaxRequestBodySize = QuestionImportService.MaxUploadBytes + 1024 * 1024;
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

            if (!int.TryParse(form["certId"], out var certId))
            {
                return Problem(StatusCodes.Status400BadRequest, "Choose a certificate.", ApiError.Codes.Validation, "Choose the certificate the questions belong to.");
            }

            try
            {
                await using var stream = file.OpenReadStream();
                var result = await imports.ImportAsync(CurrentUserId(http), certId, file.FileName, stream, cancellationToken);
                return Results.Ok(result);
            }
            catch (AdminExamException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        }).DisableAntiforgery();

        // GET /api/media/imports/{batchId}/{fileName}: imported audio and images for any signed-in
        // user (learners need them to sit the exam). The store resolves the path inside its own
        // root, and only names this importer generates are accepted.
        app.MapGet("/api/media/imports/{batchId:int}/{fileName}", async Task<IResult> (
            int batchId,
            string fileName,
            IFileStore fileStore,
            CancellationToken cancellationToken) =>
        {
            if (!IsGeneratedName(fileName))
            {
                return Results.NotFound();
            }

            var info = await fileStore.GetMediaAsync($"imports/{batchId}/{fileName}", cancellationToken);
            return info is null
                ? Results.NotFound()
                : Results.Stream(info.Stream, info.ContentType, enableRangeProcessing: true);
        }).RequireAuthorization();
    }

    /// <summary>32 hex characters and an allow-listed extension, as LocalFileStore.SaveImportMediaAsync names them.</summary>
    private static bool IsGeneratedName(string fileName)
    {
        var dot = fileName.IndexOf('.');
        return dot == 32
            && fileName[..dot].All(Uri.IsHexDigit)
            && fileName[dot..] is ".mp3" or ".wav" or ".jpg" or ".jpeg" or ".png";
    }
}
