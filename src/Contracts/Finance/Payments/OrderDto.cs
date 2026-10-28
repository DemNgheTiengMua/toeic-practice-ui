
namespace Contracts;

public sealed record OrderDto(
    int OrderId,
    string OrderCode,
    long PaymentNo,
    int PackageId,
    string PackageName,
    int Credits,
    decimal Amount,
    OrderStatusDto Status,
    PaymentGatewayDto Gateway,
    string? GatewayTxnRef,
    string? GatewayErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    string? CheckoutUrl = null);
