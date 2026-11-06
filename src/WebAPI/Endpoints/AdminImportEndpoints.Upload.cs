using Contracts;
using Microsoft.AspNetCore.Http.Features;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminImportEndpoints
{
    private static void MapUpload(RouteGroupBuilder admin)
    {
        // POST /api/admin/imports/pool?skill=reading  (#198)
        //
        // The seam the ticket asks for: a file for one skill in, one result per record out, with the
        // rule and the line for every refusal. It is a separate route from the exam import above
        // because the two answer different questions - this one writes pool stock, that one creates a
        // draft exam - and a shared route would have to guess which format it was given.
        admin.MapPost("/pool", async Task<IResult> (
            HttpRequest request,
            string skill,
            HttpContext http,
            PoolIngestionService ingestion,
            CancellationToken cancellationToken) =>
        {
            var bodySize = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodySize is { IsReadOnly: false })
            {
                bodySize.MaxRequestBodySize = QuestionImportService.MaxUploadBytes;
            }

            if (!request.HasFormContentType)
            {
                return Problem(StatusCodes.Status400BadRequest, "Expected multipart/form-data content.", ApiError.Codes.Validation);
            }

            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
            {
                return Problem(StatusCodes.Status400BadRequest, "Choose a file to ingest.", ApiError.Codes.Validation, "Choose a skill content pool file to ingest.");
            }

            await using var stream = file.OpenReadStream();
            var result = await ingestion.IngestAsync(skill, stream, file.FileName, cancellationToken);

            // 200 with a per-record report, never 400 for a file whose records were partly refused: the
            // valid ones were ingested, and an error status would tell a client to discard that fact.
            return Results.Ok(result);
        }).DisableAntiforgery();

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
    }
}
