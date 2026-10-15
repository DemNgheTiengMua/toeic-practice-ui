namespace Contracts;

public sealed record CheckoutPreviewDto(
    int PackageId,
    string PackageName,
    int Credits,
    decimal Price,
    int CurrentBalance,
    int BalanceAfter,
    OrderDto? OpenOrder = null);

public sealed record CreateOrderRequest(int PackageId);

public sealed record CreateOrderResponse(
    int OrderId,
    string OrderCode,
    long PaymentNo,
    decimal Amount,
    PaymentGatewayDto Gateway,
    string CheckoutUrl,
    OrderStatusDto Status);

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
    DateTimeOffset? PaidAt);

public sealed record HandleReturnRequest(string OrderCode);

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
    string? Message = null);

public sealed record WalletDto(
    int Balance,
    int CreditsPurchased,
    int CreditsUsed,
    decimal AmountPurchased,
    int PaidOrderCount,
    IReadOnlyList<OrderDto> Orders,
    IReadOnlyList<CreditLedgerEntryDto> Ledger);

public sealed record CreditLedgerEntryDto(
    int TxnId,
    CreditReasonDto Reason,
    int Delta,
    string? Note,
    DateTimeOffset CreatedAt,
    string? OrderCode);

public sealed record MockGatewayActionRequest(
    string OrderCode,
    string Action,
    string? ErrorCode = null);
