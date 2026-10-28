
namespace Contracts;

public sealed record CheckoutPreviewDto(
    int PackageId,
    string PackageName,
    int Credits,
    decimal Price,
    int CurrentBalance,
    int BalanceAfter,
    OrderDto? OpenOrder = null);
