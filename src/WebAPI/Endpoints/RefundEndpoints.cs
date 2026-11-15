using BusinessObjects;
using Contracts;
using Services;
using WebAPI.Filters;
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
        MapLookups(admin);
    }

    /// <summary>
    /// #345: the three read-only lookups the desk cannot work without - search a learner, read that
    /// learner, and list their paid orders.
    /// <para>
    /// <b>Why they live here and not on <c>/api/admin/users</c> / <c>/api/admin/orders</c>.</b> Those
    /// routes are gated <see cref="Permissions.SystemAdmin"/> because they are the governance screens:
    /// they list every learner, lock accounts, adjust wallets and reconcile payments. A support-only
    /// agent got a 403 on the picker, on the balance and on the complaint's carried learner, so the desk
    /// was usable only by an account holding all five namespaces.
    /// </para>
    /// <para>
    /// The seam is a lookup the refund desk declares for itself, under the capability that already owns
    /// refunds. It reuses the governance services rather than reimplementing them, and it exposes reads
    /// only - no lock, no credit adjustment, no reconciliation. Widening
    /// <c>system.governance.admin</c> or moving its read routes to a shared group would have handed support
    /// the governance screen's write surface with it.
    /// </para>
    /// </summary>
    private static void MapLookups(RouteGroupBuilder refunds)
    {
        refunds.MapGet("/lookups/learners", async (
            string? q,
            [AsParameters] PaginationQuery pagination,
            IAdminUserService users,
            CancellationToken cancellationToken) =>
        {
            var summary = await users.GetUsersAsync(q, status: null, pagination.PageOrDefault, pagination.PageSizeOrDefault, cancellationToken);
            return Results.Ok(summary);
        }).AddEndpointFilter<RequestValidationFilter<PaginationQuery>>();

        // One learner, by identifier, for the balance and identity the desk displays. It returns the same
        // AdminUserDto the governance screen's list uses rather than a narrower type of its own: the desk
        // needs the balance, and a second shape would be a second thing to keep in step. The placement
        // history the governance *detail* carries is dropped, which is the only part with no refund use.
        refunds.MapGet("/lookups/learners/{userId:int}", async (
            int userId,
            IAdminUserService users,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var detail = await users.GetUserDetailAsync(userId, cancellationToken);
                return Results.Ok(detail.User);
            }
            catch (AdminUserException ex)
            {
                return Problem(ex.Status, ex.Message, ex.Code);
            }
        });

        // That learner's paid orders. Paid only: a refund against an unpaid order would return money
        // that never arrived, so the route does not offer the other statuses at all.
        refunds.MapGet("/lookups/orders", async (
            int userId,
            [AsParameters] PaginationQuery pagination,
            IAdminOrderService orders,
            CancellationToken cancellationToken) =>
        {
            var summary = await orders.GetOrdersAsync(
                q: null,
                status: OrderStatus.Paid.ToCode(),
                gateway: null,
                userId: userId,
                pagination.PageOrDefault,
                pagination.PageSizeOrDefault,
                cancellationToken);
            return Results.Ok(summary);
        }).AddEndpointFilter<RequestValidationFilter<PaginationQuery>>();
    }

    private static void MapAdmin(RouteGroupBuilder admin)
    {
        admin.MapGet("", async (
            string? q,
            string? status,
            [AsParameters] PaginationQuery pagination,
            IRefundService refunds,
            CancellationToken cancellationToken) =>
        {
            var summary = await refunds.GetAdminRefundsAsync(q, status, pagination.PageOrDefault, pagination.PageSizeOrDefault, cancellationToken);
            return Results.Ok(summary);
        }).AddEndpointFilter<RequestValidationFilter<PaginationQuery>>();

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
        }).AddEndpointFilter<RequestValidationFilter<RequestRefundRequest>>();

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
        }).AddEndpointFilter<RequestValidationFilter<RejectRefundRequest>>();

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
