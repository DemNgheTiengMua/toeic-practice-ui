namespace Contracts;

public sealed record CheckoutPreviewDto(
    int Credits,
    decimal Price,
    int CurrentBalance,
    int BalanceAfter,
    OrderDto? OpenOrder = null);
