using BusinessObjects;
using Contracts;
using Microsoft.AspNetCore.Http.Features;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

/// <summary>
/// #205 / S-02: the ingestion and staging workbench.
/// <para>
/// <b>Two policies, not one.</b> Uploading and re-submitting a record is authoring
/// (<c>pool.content.author</c>); clearing a staged item is review (<c>pool.content.review</c>). D16
/// splits them because "may write a record" and "may publish it" are different authorities, and ADM-02
/// makes the second one refuse the account that did the first.
/// </para>
/// <para>
/// The uploader is <see cref="IAdminIngestionService"/>'s caller account, read from
/// <see cref="IActingStaffAccount"/> - the same source the stamp interceptor uses, so the batch's
/// <c>UploadedBy</c> and the items' <c>LastWrittenBy</c> cannot name different accounts.
/// </para>
/// </summary>
public static class AdminIngestionEndpoints
{
    public static void MapAdminIngestionEndpoints(this IEndpointRouteBuilder app)
    {
        var author = app.MapGroup("/api/admin/ingestion").RequireAuthorization(Permissions.ContentAuthor);
        var review = app.MapGroup("/api/admin/ingestion").RequireAuthorization(Permissions.ContentReview);

        author.MapGet("", async (IAdminIngestionService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetBatchesAsync(cancellationToken)));

        author.MapGet("/{batchId:int}", async (
            int batchId, IAdminIngestionService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.GetBatchAsync(batchId, cancellationToken));
            }
            catch (AdminItemException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        author.MapPost("/upload", async Task<IResult> (
            HttpRequest request,
            string skill,
            IAdminIngestionService service,
            IActingStaffAccount acting,
            HttpContext http,
            CancellationToken cancellationToken) =>
        {
            var bodySize = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodySize is { IsReadOnly: false })
            {
                bodySize.MaxRequestBodySize = 100L * 1024 * 1024;
            }

            if (acting.UserId is not int uploadedBy)
            {
                return Problem(StatusCodes.Status401Unauthorized, "The acting staff account is unknown.",
                    ApiError.Codes.Unauthenticated);
            }

            if (!request.HasFormContentType)
            {
                return Problem(StatusCodes.Status400BadRequest, "Expected multipart/form-data content.",
                    ApiError.Codes.Validation);
            }

            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
            {
                return Problem(StatusCodes.Status400BadRequest, "Choose a file to ingest.",
                    ApiError.Codes.Validation, "Choose a skill content pool file to ingest.");
            }

            try
            {
                await using var stream = file.OpenReadStream();
                var result = await service.UploadAsync(skill, stream, file.FileName, uploadedBy, cancellationToken);
                return Results.Ok(result);
            }
            catch (AdminItemException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        }).DisableAntiforgery();

        author.MapPost("/{batchId:int}/records/{line:int}/resubmit", async (
            int batchId,
            int line,
            IngestionResubmitRequest request,
            IAdminIngestionService service,
            IActingStaffAccount acting,
            CancellationToken cancellationToken) =>
        {
            if (acting.UserId is not int submittedBy)
            {
                return Problem(StatusCodes.Status401Unauthorized, "The acting staff account is unknown.",
                    ApiError.Codes.Unauthenticated);
            }

            try
            {
                return Results.Ok(await service.ResubmitAsync(
                    batchId, line, request.Record ?? string.Empty, submittedBy, cancellationToken));
            }
            catch (AdminItemException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        // Clearing is review, and it is the act that makes staged content live.
        review.MapPost("/{batchId:int}/items/{itemId:int}/clear", async (
            int batchId,
            int itemId,
            IAdminIngestionService service,
            IActingStaffAccount acting,
            CancellationToken cancellationToken) =>
        {
            if (acting.UserId is not int clearedBy)
            {
                return Problem(StatusCodes.Status401Unauthorized, "The acting staff account is unknown.",
                    ApiError.Codes.Unauthenticated);
            }

            try
            {
                await service.ClearAsync(batchId, itemId, clearedBy, cancellationToken);
                return Results.NoContent();
            }
            catch (AdminItemException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });
    }
}

/// <summary>#205: the corrected record, exactly as the author edited it.</summary>
public sealed record IngestionResubmitRequest(string? Record);
