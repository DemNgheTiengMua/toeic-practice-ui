using BusinessObjects;

namespace Services;

public sealed record WebhookVerificationResult(
    bool IsValid,
    long PaymentNo,
    string? GatewayTxnRef,
    GatewayStatus Status,
    string? ErrorCode,
    string RawPayload);
