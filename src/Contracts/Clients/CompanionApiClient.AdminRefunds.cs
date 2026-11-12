
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

    /// <summary>
    /// #283: the export is streamed rather than buffered - it is a file download, so the whole CSV does not
    /// need to sit in memory twice. The shape is the one every other export on this client uses
    /// (<see cref="ExportAdminExamAsync"/>), so a controller relays it the same way.
    /// </summary>
    public async Task<(Stream Stream, string ContentType, string FileName)> ExportAdminRefundsAsync(
        DateOnly from, DateOnly to, string bearerToken, CancellationToken cancellationToken = default)
    {
        var path = $"api/admin/refunds/export?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        AttachBearer(request, bearerToken);

        var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try
        {
            await EnsureSuccessAsync(response, cancellationToken);
        }
        catch
        {
            response.Dispose();
            throw;
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? RefundExportCsv.ContentType;
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName
            ?? $"refunds-{from:yyyy-MM-dd}-to-{to:yyyy-MM-dd}.csv";

        return (stream, contentType, fileName);
    }

    public Task<(Stream Stream, string ContentType, string FileName)> ExportAdminRefundsAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        ExportAdminRefundsAsync(from, to, string.Empty, cancellationToken);
}
