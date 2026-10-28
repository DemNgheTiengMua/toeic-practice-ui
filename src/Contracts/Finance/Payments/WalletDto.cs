
namespace Contracts;

public sealed record WalletDto(
    int Balance,
    int CreditsPurchased,
    int CreditsUsed,
    decimal AmountPurchased,
    int PaidOrderCount,
    IReadOnlyList<OrderDto> Orders,
    IReadOnlyList<CreditLedgerEntryDto> Ledger);
