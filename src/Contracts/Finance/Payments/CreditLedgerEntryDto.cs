
namespace Contracts;

public sealed record CreditLedgerEntryDto(
    int TxnId,
    CreditReasonDto Reason,
    int Delta,
    string? Note,
    DateTimeOffset CreatedAt,
    string? OrderCode);
