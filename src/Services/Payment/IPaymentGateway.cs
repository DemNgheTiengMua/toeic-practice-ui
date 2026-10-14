namespace Services;

public sealed record PaymentGatewayOrderInfo(
    long PaymentNo,
    string OrderCode,
    decimal Amount,
    string Description,
    string ReturnUrl,
    string CancelUrl);

public sealed record PaymentGatewayLinkResult(
    string CheckoutUrl,
    string? GatewayTxnRef);

public sealed record WebhookVerificationResult(
    bool IsValid,
    long PaymentNo,
    string? GatewayTxnRef,
    string Status,
    string? ErrorCode,
    string RawPayload);

public sealed record GatewayPaymentStatus(
    string Status,
    string? GatewayTxnRef,
    string? ErrorCode,
    string RawPayload);

public interface IPaymentGateway
{
    string GatewayName { get; }

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
}
