using BusinessObjects;

namespace Services;

public sealed record PaymentGatewayLinkResult(
    string CheckoutUrl,
    string? GatewayTxnRef);
