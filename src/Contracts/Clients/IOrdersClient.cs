
namespace Contracts;

public interface IOrdersClient
{
    Task<IReadOnlyList<PackageDto>> GetPublicPackagesAsync(CancellationToken cancellationToken = default);
    Task<CheckoutPreviewDto> GetCheckoutPreviewAsync(int packageId, CancellationToken cancellationToken = default);
    Task<CheckoutPreviewDto> GetCheckoutPreviewAsync(int packageId, string bearerToken, CancellationToken cancellationToken = default) => GetCheckoutPreviewAsync(packageId, cancellationToken);
    Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default);
    Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request, string bearerToken, CancellationToken cancellationToken = default) => CreateOrderAsync(request, cancellationToken);
    Task<PaymentResultDto> HandleReturnAsync(HandleReturnRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResultDto> HandleReturnAsync(HandleReturnRequest request, string bearerToken, CancellationToken cancellationToken = default) => HandleReturnAsync(request, cancellationToken);
    Task<PaymentResultDto> GetPaymentResultAsync(string orderCode, CancellationToken cancellationToken = default);
    Task<PaymentResultDto> GetPaymentResultAsync(string orderCode, string bearerToken, CancellationToken cancellationToken = default) => GetPaymentResultAsync(orderCode, cancellationToken);
    Task CancelOrderAsync(CancelOrderRequest request, CancellationToken cancellationToken = default);
    Task CancelOrderAsync(CancelOrderRequest request, string bearerToken, CancellationToken cancellationToken = default) => CancelOrderAsync(request, cancellationToken);
    Task<PaymentResultDto> ApplyMockGatewayActionAsync(MockGatewayActionRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResultDto> ApplyMockGatewayActionAsync(MockGatewayActionRequest request, string bearerToken, CancellationToken cancellationToken = default) => ApplyMockGatewayActionAsync(request, cancellationToken);
}
