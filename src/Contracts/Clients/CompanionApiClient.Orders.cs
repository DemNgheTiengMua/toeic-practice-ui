using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{

    public async Task<IReadOnlyList<PackageDto>> GetPublicPackagesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync("api/packages", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<PackageDto>>(Json, cancellationToken))!;
    }

    public Task<CheckoutPreviewDto> GetCheckoutPreviewAsync(int packageId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<CheckoutPreviewDto>($"api/checkout/preview?packageId={packageId}", bearerToken, cancellationToken);

    public async Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/checkout/orders")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<CreateOrderResponse>(Json, cancellationToken))!;
    }

    public async Task<PaymentResultDto> HandleReturnAsync(HandleReturnRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/checkout/return")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<PaymentResultDto>(Json, cancellationToken))!;
    }

    public Task<PaymentResultDto> GetPaymentResultAsync(string orderCode, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<PaymentResultDto>($"api/checkout/orders/{Uri.EscapeDataString(orderCode)}/result", bearerToken, cancellationToken);

    public async Task CancelOrderAsync(CancelOrderRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/checkout/cancel")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<PaymentResultDto> ApplyMockGatewayActionAsync(MockGatewayActionRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/mock-gateway/action")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<PaymentResultDto>(Json, cancellationToken))!;
    }

    public Task<CheckoutPreviewDto> GetCheckoutPreviewAsync(int packageId, CancellationToken cancellationToken = default) =>
        GetCheckoutPreviewAsync(packageId, string.Empty, cancellationToken);

    public Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default) =>
        CreateOrderAsync(request, string.Empty, cancellationToken);

    public Task<PaymentResultDto> HandleReturnAsync(HandleReturnRequest request, CancellationToken cancellationToken = default) =>
        HandleReturnAsync(request, string.Empty, cancellationToken);

    public Task<PaymentResultDto> GetPaymentResultAsync(string orderCode, CancellationToken cancellationToken = default) =>
        GetPaymentResultAsync(orderCode, string.Empty, cancellationToken);

    public Task CancelOrderAsync(CancelOrderRequest request, CancellationToken cancellationToken = default) =>
        CancelOrderAsync(request, string.Empty, cancellationToken);

    public Task<PaymentResultDto> ApplyMockGatewayActionAsync(MockGatewayActionRequest request, CancellationToken cancellationToken = default) =>
        ApplyMockGatewayActionAsync(request, string.Empty, cancellationToken);
}
