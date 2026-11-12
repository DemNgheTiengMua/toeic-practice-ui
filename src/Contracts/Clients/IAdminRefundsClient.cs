
namespace Contracts;

public interface IAdminRefundsClient
{
    Task<AdminRefundsSummaryDto> GetAdminRefundsAsync(string? q, string? status, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AdminRefundsSummaryDto> GetAdminRefundsAsync(string? q, string? status, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default) => GetAdminRefundsAsync(q, status, page, pageSize, cancellationToken);
    Task<AdminRefundDetailDto> GetAdminRefundDetailAsync(int refundId, CancellationToken cancellationToken = default);
    Task<AdminRefundDetailDto> GetAdminRefundDetailAsync(int refundId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminRefundDetailAsync(refundId, cancellationToken);
    Task<AdminRefundDetailDto> RequestAdminRefundAsync(RequestRefundRequest request, CancellationToken cancellationToken = default);
    Task<AdminRefundDetailDto> RequestAdminRefundAsync(RequestRefundRequest request, string bearerToken, CancellationToken cancellationToken = default) => RequestAdminRefundAsync(request, cancellationToken);
    Task<AdminRefundDetailDto> ApproveAdminRefundAsync(int refundId, CancellationToken cancellationToken = default);
    Task<AdminRefundDetailDto> ApproveAdminRefundAsync(int refundId, string bearerToken, CancellationToken cancellationToken = default) => ApproveAdminRefundAsync(refundId, cancellationToken);
    Task<AdminRefundDetailDto> RejectAdminRefundAsync(int refundId, RejectRefundRequest request, CancellationToken cancellationToken = default);
    Task<AdminRefundDetailDto> RejectAdminRefundAsync(int refundId, RejectRefundRequest request, string bearerToken, CancellationToken cancellationToken = default) => RejectAdminRefundAsync(refundId, request, cancellationToken);
    Task<AdminRefundDetailDto> SettleAdminRefundAsync(int refundId, CancellationToken cancellationToken = default);
    Task<AdminRefundDetailDto> SettleAdminRefundAsync(int refundId, string bearerToken, CancellationToken cancellationToken = default) => SettleAdminRefundAsync(refundId, cancellationToken);

    /// <summary>#283: the accounting CSV for a settlement-date range, as a download.</summary>
    Task<(Stream Stream, string ContentType, string FileName)> ExportAdminRefundsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
    Task<(Stream Stream, string ContentType, string FileName)> ExportAdminRefundsAsync(DateOnly from, DateOnly to, string bearerToken, CancellationToken cancellationToken = default) => ExportAdminRefundsAsync(from, to, cancellationToken);
}
