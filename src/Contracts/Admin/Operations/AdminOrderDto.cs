
namespace Contracts;

public sealed record AdminOrderDto(
    int OrderId,
    string OrderCode,
    long PaymentNo,
    int UserId,
    string UserEmail,
    string? UserFullName,
    int PackageId,
    string PackageName,
    int Credits,
    decimal Amount,
    OrderStatusDto Status,
    PaymentGatewayDto Gateway,
    string? GatewayTxnRef,
    string? GatewayErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt);
