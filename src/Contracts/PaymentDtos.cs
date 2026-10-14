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
    string Gateway,
    string CheckoutUrl,
    string Status);

public sealed record OrderDto(
    int OrderId,
    string OrderCode,
    long PaymentNo,
    int PackageId,
    string PackageName,
    int Credits,
    decimal Amount,
    string Status,
    string Gateway,
    string? GatewayTxnRef,
    string? GatewayErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt);

public sealed record HandleReturnRequest(string OrderCode);

public sealed record PaymentResultDto(
    int OrderId,
    string OrderCode,
    string Status,
    string PackageName,
    int Credits,
    decimal Amount,
    string Gateway,
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
    string Reason,
    int Delta,
    string? Note,
    DateTimeOffset CreatedAt,
    string? OrderCode);

public sealed record MockGatewayActionRequest(
    string OrderCode,
    string Action,
    string? ErrorCode = null);
