
namespace Contracts;

public interface IOrdersClient
{
    Task<CheckoutPreviewDto> GetCheckoutPreviewAsync(int credits, CancellationToken cancellationToken = default);
    Task<CheckoutPreviewDto> GetCheckoutPreviewAsync(int credits, string bearerToken, CancellationToken cancellationToken = default) => GetCheckoutPreviewAsync(credits, cancellationToken);
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
