
namespace Contracts;

public interface IAdminOrdersClient
{
    Task<AdminOrdersSummaryDto> GetAdminOrdersAsync(string? q, string? status, string? gateway, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminOrdersSummaryDto> GetAdminOrdersAsync(string? q, string? status, string? gateway, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default) => GetAdminOrdersAsync(q, status, gateway, page, pageSize, cancellationToken);
    Task<AdminOrderDetailDto> GetAdminOrderDetailAsync(int orderId, CancellationToken cancellationToken = default);
    Task<AdminOrderDetailDto> GetAdminOrderDetailAsync(int orderId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminOrderDetailAsync(orderId, cancellationToken);
    Task<PaymentResultDto> ReconcileAdminOrderAsync(int orderId, CancellationToken cancellationToken = default);
    Task<PaymentResultDto> ReconcileAdminOrderAsync(int orderId, string bearerToken, CancellationToken cancellationToken = default) => ReconcileAdminOrderAsync(orderId, cancellationToken);
}
