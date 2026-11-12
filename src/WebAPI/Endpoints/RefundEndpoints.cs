using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

/// <summary>
/// #281: the refund routes. Gated on <see cref="Permissions.SupportTriage"/>, the same namespace the
/// complaints admin routes use - a refund is support work, not system governance.
/// </summary>
public static partial class RefundEndpoints
{
    public static void MapRefundEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/refunds").RequireAuthorization(Permissions.SupportTriage);
        MapAdmin(admin);
    }

    private static void MapAdmin(RouteGroupBuilder admin)
    {
        admin.MapGet("", async (
            string? q,
            string? status,
            int? page,
            int? pageSize,
            IRefundService refunds,
            CancellationToken cancellationToken) =>
        {
            var summary = await refunds.GetAdminRefundsAsync(q, status, page ?? 1, pageSize ?? 20, cancellationToken);
            return Results.Ok(summary);
        });

        admin.MapGet("/{refundId:int}", async (
            int refundId,
            IRefundService refunds,
            CancellationToken cancellationToken) =>
        {
            var detail = await refunds.GetAdminRefundDetailAsync(refundId, cancellationToken);
            return detail is not null ? Results.Ok(detail) : Results.NotFound();
        });

        admin.MapPost("", async (
            RequestRefundRequest request,
            HttpContext http,
            IRefundService refunds,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var created = await refunds.RequestRefundAsync(CurrentUserId(http), request, cancellationToken);
                return Results.Ok(created);
            }
            catch (RefundNotFoundException)
            {
                return Results.NotFound();
            }
            catch (RefundException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/{refundId:int}/approve", async (
            int refundId,
            HttpContext http,
            IRefundService refunds,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var approved = await refunds.ApproveRefundAsync(CurrentUserId(http), refundId, cancellationToken);
                return Results.Ok(approved);
            }
            catch (RefundNotFoundException)
            {
                return Results.NotFound();
            }
            catch (RefundException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        admin.MapPost("/{refundId:int}/reject", async (
            int refundId,
            RejectRefundRequest request,
            HttpContext http,
            IRefundService refunds,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var rejected = await refunds.RejectRefundAsync(
                    CurrentUserId(http), refundId, request.Reason, cancellationToken);
                return Results.Ok(rejected);
            }
            catch (RefundNotFoundException)
            {
                return Results.NotFound();
            }
            catch (RefundException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        // #282: the accountant's step. A separate route because it is a separate decision point: an
        // approval says "pay this", a settlement says "it was paid", and the second is what counts.
        admin.MapPost("/{refundId:int}/settle", async (
            int refundId,
            HttpContext http,
            IRefundService refunds,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var settled = await refunds.SettleRefundAsync(CurrentUserId(http), refundId, cancellationToken);
                return Results.Ok(settled);
            }
            catch (RefundNotFoundException)
            {
                return Results.NotFound();
            }
            catch (RefundException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });

        // #283: the accountant's batch file. A GET because it reads: exporting twice must yield the same
        // rows, so there is no state for a second call to disturb.
        admin.MapGet("/export", async (
            DateOnly? from,
            DateOnly? to,
            IRefundService refunds,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            try
            {
                // No range means "today", the day accounting is most often paying for, rather than an
                // unbounded file that would export the company's whole refund history by accident.
                var now = ReportingPeriod.LocalInstant(clock.GetUtcNow());
                var toDay = to ?? DateOnly.FromDateTime(now.DateTime);
                var fromDay = from ?? toDay;

                var rows = await refunds.GetRefundExportRowsAsync(fromDay, toDay, cancellationToken);
                var bytes = RefundExportCsv.Format(rows, now);

                return Results.File(
                    bytes,
                    RefundExportCsv.ContentType,
                    $"refunds-{fromDay:yyyy-MM-dd}-to-{toDay:yyyy-MM-dd}.csv");
            }
            catch (RefundException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code, ex.Message);
            }
        });
    }
}
