using BusinessObjects;

namespace Services;

public sealed record GatewayPaymentStatus(
    GatewayStatus Status,
    string? GatewayTxnRef,
    string? ErrorCode,
    string RawPayload);
