using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using static Contracts.EnumMapping;

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
