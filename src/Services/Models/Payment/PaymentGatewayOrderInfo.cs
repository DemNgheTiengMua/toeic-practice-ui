using BusinessObjects;

namespace Services;

public sealed record PaymentGatewayOrderInfo(
    long PaymentNo,
    string OrderCode,
    decimal Amount,
    string Description,
    string ReturnUrl,
    string CancelUrl);
