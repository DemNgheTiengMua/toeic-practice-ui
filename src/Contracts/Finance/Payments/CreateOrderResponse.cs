
namespace Contracts;

public sealed record CreateOrderResponse(
    int OrderId,
    string OrderCode,
    long PaymentNo,
    decimal Amount,
    PaymentGatewayDto Gateway,
    string CheckoutUrl,
    OrderStatusDto Status);
