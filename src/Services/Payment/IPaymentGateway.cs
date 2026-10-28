using BusinessObjects;
namespace Services;

public interface IPaymentGateway
{
    PaymentGateway GatewayName { get; }

    Task<PaymentGatewayLinkResult> CreatePaymentLinkAsync(
        PaymentGatewayOrderInfo orderInfo,
        CancellationToken cancellationToken = default);

    Task<WebhookVerificationResult> VerifyWebhookAsync(
        string payload,
        string? signature,
        CancellationToken cancellationToken = default);

    Task<GatewayPaymentStatus> QueryPaymentStatusAsync(
        long paymentNo,
        string? gatewayTxnRef,
        CancellationToken cancellationToken = default);

    Task<bool> CancelPaymentLinkAsync(
        long paymentNo,
        string? gatewayTxnRef,
        string? cancellationReason = null,
        CancellationToken cancellationToken = default);
}
