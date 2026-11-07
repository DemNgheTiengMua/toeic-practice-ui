using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

/// <summary>
/// #206 / S-03: the stratified audit and quarantine queue.
/// <para>
/// <b>Two capabilities again, and they are not the same act.</b> Reading the queue and recording a
/// solve or a verdict is review (<c>pool.content.review</c>); drawing a sample is the same review, and
/// so is taking the decision. Nothing here is authoring: an author who uploaded the batch cannot audit
/// it, which is ADM-02's first arm and the reason the capability split exists.
/// </para>
/// <para>
/// <b>The reveal is its own route.</b> If <c>GET .../batches/{id}</c> returned keys, blindness would be
/// a property of which fields a page chose to render. It returns none, and
/// <c>POST .../samples/{id}/reveal</c> refuses until a solve is stored - so the order is enforced on
/// the read, which is where a caller who never opens the screen also meets it.
/// </para>
/// </summary>
public static class AdminAuditEndpoints
{
    public static void MapAdminAuditEndpoints(this IEndpointRouteBuilder app)
    {
        // Review-only: every verb changes, or reads, a judgement about content.
        var review = app.MapGroup("/api/admin/audit").RequireAuthorization(Permissions.ContentReview);

        review.MapGet("/batches/{batchId:int}", async (
            int batchId, IAdminAuditService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.GetQueueAsync(batchId, cancellationToken));
            }
            catch (AdminItemException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        review.MapPost("/batches/{batchId:int}/draw", async (
            int batchId,
            IAdminAuditService service,
            IActingStaffAccount acting,
            CancellationToken cancellationToken) =>
        {
            if (acting.UserId is not int drawnBy)
            {
                return Problem(StatusCodes.Status401Unauthorized, "The acting staff account is unknown.",
                    ApiError.Codes.Unauthenticated);
            }

            try
            {
                return Results.Ok(await service.DrawAsync(batchId, drawnBy, cancellationToken));
            }
            catch (AdminItemException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        review.MapPost("/samples/{sampleItemId:int}/solve", async (
            int sampleItemId,
            AuditSolveRequest request,
            IAdminAuditService service,
            IActingStaffAccount acting,
            CancellationToken cancellationToken) =>
        {
            if (acting.UserId is not int solvedBy)
            {
                return Problem(StatusCodes.Status401Unauthorized, "The acting staff account is unknown.",
                    ApiError.Codes.Unauthenticated);
            }

            try
            {
                await service.RecordSolveAsync(sampleItemId, request.Label ?? string.Empty, solvedBy, cancellationToken);
                return Results.NoContent();
            }
            catch (AdminItemException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        // The one route that returns an authored key, and therefore the one that carries W-01.
        review.MapPost("/samples/{sampleItemId:int}/reveal", async (
            int sampleItemId, IAdminAuditService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.RevealKeyAsync(sampleItemId, cancellationToken));
            }
            catch (AdminItemException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        review.MapPost("/samples/{sampleItemId:int}/verdict", async (
            int sampleItemId,
            AuditVerdictRequest request,
            IAdminAuditService service,
            IActingStaffAccount acting,
            CancellationToken cancellationToken) =>
        {
            if (acting.UserId is not int reviewedBy)
            {
                return Problem(StatusCodes.Status401Unauthorized, "The acting staff account is unknown.",
                    ApiError.Codes.Unauthenticated);
            }

            try
            {
                await service.RecordVerdictAsync(sampleItemId, request.Verdict ?? string.Empty, reviewedBy, cancellationToken);
                return Results.NoContent();
            }
            catch (AdminItemException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        review.MapPost("/strata/{stratumId:int}/decide", async (
            int stratumId,
            IAdminAuditService service,
            IActingStaffAccount acting,
            CancellationToken cancellationToken) =>
        {
            if (acting.UserId is not int decidedBy)
            {
                return Problem(StatusCodes.Status401Unauthorized, "The acting staff account is unknown.",
                    ApiError.Codes.Unauthenticated);
            }

            try
            {
                return Results.Ok(await service.DecideAsync(stratumId, decidedBy, cancellationToken));
            }
            catch (AdminItemException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });
    }
}
