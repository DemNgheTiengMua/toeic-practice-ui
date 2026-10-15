using BusinessObjects;
using Contracts;

namespace Services;

public interface IPaymentService
{
    Task<CheckoutPreviewDto> GetCheckoutPreviewAsync(
        int userId,
        int packageId,
        CancellationToken cancellationToken = default);

    Task<CreateOrderResponse> CreateOrderAsync(
        int userId,
        int packageId,
        string baseUrl,
        CancellationToken cancellationToken = default);

    Task<PaymentResultDto> HandleReturnRedirectAsync(
        int userId,
        string orderCode,
        CancellationToken cancellationToken = default);

    Task CancelOrderAsync(
        int userId,
        string orderCode,
        CancellationToken cancellationToken = default);

    Task<PaymentResultDto> GetPaymentResultAsync(
        int userId,
        string orderCode,
        CancellationToken cancellationToken = default);

    Task<bool> SettleOrderAsync(
        long paymentNo,
        PaymentEventSource source,
        bool signatureValid,
        string? rawPayload = null,
        string? note = null,
        CancellationToken cancellationToken = default);

    Task<WebhookVerificationResult> HandleWebhookAsync(
        string payload,
        string? signature,
        CancellationToken cancellationToken = default);

    Task<WalletDto> GetWalletAsync(
        int userId,
        CancellationToken cancellationToken = default);

    Task ExpireStaleOrdersAsync(
        int olderThanMinutes = 15,
        CancellationToken cancellationToken = default);

    Task<PaymentResultDto> ApplyMockGatewayActionAsync(
        int userId,
        string orderCode,
        string action,
        string? errorCode = null,
        CancellationToken cancellationToken = default);
}
