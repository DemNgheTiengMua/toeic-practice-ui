using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using static Contracts.EnumMapping;

namespace Services;

public sealed class AdminOrderService(
    CompanionDbContext db,
    IPaymentService paymentService,
    TimeProvider clock) : IAdminOrderService
{
    public async Task<AdminOrdersSummaryDto> GetOrdersAsync(
        string? q = null,
        string? status = null,
        string? gateway = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        await paymentService.ExpireStaleOrdersAsync(olderThanMinutes: 15, cancellationToken);

        var now = clock.GetUtcNow();
        var startOfMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

        var paidThisMonth = await db.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.Paid.ToCode() && o.PaidAt >= startOfMonth)
            .Select(o => o.Amount)
            .ToListAsync(cancellationToken);

        var paidThisMonthCount = paidThisMonth.Count;
        var paidThisMonthAmount = paidThisMonth.Sum();

        var reconcilingCount = await db.Orders
            .AsNoTracking()
            .CountAsync(o => o.Status == OrderStatus.Reconciling.ToCode(), cancellationToken);

        var failedOrExpiredCount = await db.Orders
            .AsNoTracking()
            .CountAsync(o => o.Status == OrderStatus.Failed.ToCode() || o.Status == OrderStatus.Expired.ToCode(), cancellationToken);

        IQueryable<Order> query = db.Orders
            .AsNoTracking()
            .Include(o => o.User)
            .Include(o => o.Package);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var trimmed = q.Trim().ToLower();
            query = query.Where(o =>
                o.OrderCode.ToLower().Contains(trimmed) ||
                o.User.Email.ToLower().Contains(trimmed) ||
                (o.User.FullName != null && o.User.FullName.ToLower().Contains(trimmed)));
        }

        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            // An unknown filter value matches nothing, as the raw string compare did before.
            var statusCode = EnumCodes.TryParse<OrderStatus>(status.Trim(), out var parsedStatus) ? parsedStatus.ToCode() : null;
            query = query.Where(o => o.Status == statusCode);
        }

        if (!string.IsNullOrWhiteSpace(gateway) && !gateway.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var gatewayCode = EnumCodes.TryParse<PaymentGateway>(gateway.Trim(), out var parsedGateway) ? parsedGateway.ToCode() : null;
            query = query.Where(o => o.Gateway == gatewayCode);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new AdminOrderDto(
                o.OrderId,
                o.OrderCode,
                o.PaymentNo,
                o.UserId,
                o.User.Email,
                o.User.FullName,
                o.PackageId,
                o.Package.Name,
                o.Package.Credits,
                o.Amount,
                ParseWireCode<OrderStatusDto>(o.Status),
                ParseWireCode<PaymentGatewayDto>(o.Gateway),
                o.GatewayTxnRef,
                o.GatewayErrorCode,
                o.CreatedAt,
                o.PaidAt))
            .ToListAsync(cancellationToken);

        return new AdminOrdersSummaryDto(
            orders,
            totalCount,
            page,
            pageSize,
            paidThisMonthCount,
            paidThisMonthAmount,
            reconcilingCount,
            failedOrExpiredCount);
    }

    public async Task<AdminOrderDetailDto?> GetOrderDetailAsync(
        int orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.User)
            .Include(o => o.Package)
            .Include(o => o.PaymentEvents)
            .SingleOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var orderDto = new AdminOrderDto(
            order.OrderId,
            order.OrderCode,
            order.PaymentNo,
            order.UserId,
            order.User.Email,
            order.User.FullName,
            order.PackageId,
            order.Package.Name,
            order.Package.Credits,
            order.Amount,
            ParseWireCode<OrderStatusDto>(order.Status),
            ParseWireCode<PaymentGatewayDto>(order.Gateway),
            order.GatewayTxnRef,
            order.GatewayErrorCode,
            order.CreatedAt,
            order.PaidAt);

        var events = order.PaymentEvents
            .OrderBy(e => e.CreatedAt)
            .ThenBy(e => e.EventId)
            .Select(e => new AdminPaymentEventDto(
                e.EventId,
                e.OrderId,
                ParseWireCode<PaymentEventSourceDto>(e.Source),
                e.ReportedStatus is null ? null : ParseWireCode<GatewayStatusDto>(e.ReportedStatus),
                e.SignatureValid,
                e.WasApplied,
                e.Note,
                e.Payload,
                e.CreatedAt))
            .ToList();

        return new AdminOrderDetailDto(orderDto, events);
    }

    public async Task<PaymentResultDto> ReconcileOrderAsync(
        int orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await db.Orders
            .AsNoTracking()
            .SingleOrDefaultAsync(o => o.OrderId == orderId, cancellationToken);

        if (order is null)
        {
            throw new OrderNotFoundException(orderId.ToString());
        }

        return await paymentService.GetPaymentResultAsync(order.UserId, order.OrderCode, cancellationToken);
    }
}
