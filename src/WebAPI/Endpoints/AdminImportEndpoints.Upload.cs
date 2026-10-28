using Contracts;
using Microsoft.AspNetCore.Http.Features;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class AdminImportEndpoints
{
    private static void MapUpload(RouteGroupBuilder admin)
    {

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
