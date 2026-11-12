
namespace Contracts;

public sealed partial class CompanionApiClient
{
    public Task<AdminRefundsSummaryDto> GetAdminRefundsAsync(string? q, string? status, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(q)) query.Add($"q={Uri.EscapeDataString(q)}");
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
        var path = $"api/admin/refunds?{string.Join("&", query)}";
        return GetAuthorizedAsync<AdminRefundsSummaryDto>(path, bearerToken, cancellationToken);
    }

    public Task<AdminRefundDetailDto> GetAdminRefundDetailAsync(int refundId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AdminRefundDetailDto>($"api/admin/refunds/{refundId}", bearerToken, cancellationToken);

    public Task<AdminRefundDetailDto> RequestAdminRefundAsync(RequestRefundRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<RequestRefundRequest, AdminRefundDetailDto>("api/admin/refunds", request, bearerToken, cancellationToken);

    public Task<AdminRefundDetailDto> ApproveAdminRefundAsync(int refundId, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<AdminRefundDetailDto>($"api/admin/refunds/{refundId}/approve", bearerToken, cancellationToken);

    public Task<AdminRefundDetailDto> RejectAdminRefundAsync(int refundId, RejectRefundRequest request, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<RejectRefundRequest, AdminRefundDetailDto>($"api/admin/refunds/{refundId}/reject", request, bearerToken, cancellationToken);

    public Task<AdminRefundDetailDto> SettleAdminRefundAsync(int refundId, string bearerToken, CancellationToken cancellationToken = default) =>
        PostAuthorizedAsync<AdminRefundDetailDto>($"api/admin/refunds/{refundId}/settle", bearerToken, cancellationToken);

    public Task<AdminRefundsSummaryDto> GetAdminRefundsAsync(string? q, string? status, int page, int pageSize, CancellationToken cancellationToken = default) =>
        GetAdminRefundsAsync(q, status, page, pageSize, string.Empty, cancellationToken);

    public Task<AdminRefundDetailDto> GetAdminRefundDetailAsync(int refundId, CancellationToken cancellationToken = default) =>
        GetAdminRefundDetailAsync(refundId, string.Empty, cancellationToken);

    public Task<AdminRefundDetailDto> RequestAdminRefundAsync(RequestRefundRequest request, CancellationToken cancellationToken = default) =>
        RequestAdminRefundAsync(request, string.Empty, cancellationToken);

    public Task<AdminRefundDetailDto> ApproveAdminRefundAsync(int refundId, CancellationToken cancellationToken = default) =>
        ApproveAdminRefundAsync(refundId, string.Empty, cancellationToken);

    public Task<AdminRefundDetailDto> RejectAdminRefundAsync(int refundId, RejectRefundRequest request, CancellationToken cancellationToken = default) =>
        RejectAdminRefundAsync(refundId, request, string.Empty, cancellationToken);

    public Task<AdminRefundDetailDto> SettleAdminRefundAsync(int refundId, CancellationToken cancellationToken = default) =>
        SettleAdminRefundAsync(refundId, string.Empty, cancellationToken);
}
