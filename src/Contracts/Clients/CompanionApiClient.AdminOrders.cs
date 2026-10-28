using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{

    public Task<AdminOrdersSummaryDto> GetAdminOrdersAsync(string? q, string? status, string? gateway, int page, int pageSize, string bearerToken, CancellationToken cancellationToken = default)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (!string.IsNullOrWhiteSpace(q)) query.Add($"q={Uri.EscapeDataString(q)}");
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrWhiteSpace(gateway)) query.Add($"gateway={Uri.EscapeDataString(gateway)}");
        var path = $"api/admin/orders?{string.Join("&", query)}";
        return GetAuthorizedAsync<AdminOrdersSummaryDto>(path, bearerToken, cancellationToken);
    }

    public Task<AdminOrderDetailDto> GetAdminOrderDetailAsync(int orderId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AdminOrderDetailDto>($"api/admin/orders/{orderId}", bearerToken, cancellationToken);

    public async Task<PaymentResultDto> ReconcileAdminOrderAsync(int orderId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/admin/orders/{orderId}/reconcile");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<PaymentResultDto>(Json, cancellationToken))!;
    }

    public Task<AdminOrdersSummaryDto> GetAdminOrdersAsync(string? q, string? status, string? gateway, int page, int pageSize, CancellationToken cancellationToken = default) =>
        GetAdminOrdersAsync(q, status, gateway, page, pageSize, string.Empty, cancellationToken);

    public Task<AdminOrderDetailDto> GetAdminOrderDetailAsync(int orderId, CancellationToken cancellationToken = default) =>
        GetAdminOrderDetailAsync(orderId, string.Empty, cancellationToken);

    public Task<PaymentResultDto> ReconcileAdminOrderAsync(int orderId, CancellationToken cancellationToken = default) =>
        ReconcileAdminOrderAsync(orderId, string.Empty, cancellationToken);
}
