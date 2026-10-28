
namespace Contracts;

public sealed record AdminOrderDetailDto(
    AdminOrderDto Order,
    IReadOnlyList<AdminPaymentEventDto> Events);
