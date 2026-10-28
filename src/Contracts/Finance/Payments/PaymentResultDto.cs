
namespace Contracts;

public sealed record PaymentResultDto(
    int OrderId,
    string OrderCode,
    OrderStatusDto Status,
    string PackageName,
    int Credits,
    decimal Amount,
    PaymentGatewayDto Gateway,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    int CurrentBalance,
    string? GatewayErrorCode = null,
    string? Message = null,
    string? CheckoutUrl = null,
    int PackageId = 0);
