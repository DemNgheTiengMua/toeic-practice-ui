using Contracts;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;

namespace Services;

public interface IAdminOrderService
{
    Task<AdminOrdersSummaryDto> GetOrdersAsync(
        string? q = null,
        string? status = null,
        string? gateway = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<AdminOrderDetailDto?> GetOrderDetailAsync(
        int orderId,
        CancellationToken cancellationToken = default);

    Task<PaymentResultDto> ReconcileOrderAsync(
        int orderId,
        CancellationToken cancellationToken = default);
}

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

        var now = clock.GetUtcNow();
        var startOfMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

        var paidThisMonth = await db.Orders
            .AsNoTracking()
            .Where(o => o.Status == "paid" && o.PaidAt >= startOfMonth)
            .Select(o => o.Amount)
            .ToListAsync(cancellationToken);

        var paidThisMonthCount = paidThisMonth.Count;
        var paidThisMonthAmount = paidThisMonth.Sum();

        var reconcilingCount = await db.Orders
            .AsNoTracking()
            .CountAsync(o => o.Status == "reconciling", cancellationToken);

        var failedOrExpiredCount = await db.Orders
            .AsNoTracking()
            .CountAsync(o => o.Status == "failed" || o.Status == "expired", cancellationToken);

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
            var lowerStatus = status.Trim().ToLower();
            query = query.Where(o => o.Status == lowerStatus);
        }

        if (!string.IsNullOrWhiteSpace(gateway) && !gateway.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var lowerGateway = gateway.Trim().ToLower();
            query = query.Where(o => o.Gateway == lowerGateway);
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
                o.Status,
                o.Gateway,
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
            order.Status,
            order.Gateway,
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
                e.Source,
                e.ReportedStatus,
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
            throw new ApiException(ApiError.Codes.OrderNotFound, "Order not found", 404);
        }

        return await paymentService.GetPaymentResultAsync(order.UserId, order.OrderCode, cancellationToken);
    }
}
