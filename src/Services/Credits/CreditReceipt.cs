using BusinessObjects;

namespace Services.Credits;

/// <summary>
/// Immutable confirmation generated upon posting a ledger transaction, reflecting the applied delta and resulting wallet balance.
/// </summary>
public sealed record CreditReceipt(
    int TxnId,
    int UserId,
    CreditReason Reason,
    int Delta,
    int BalanceAfter,
    DateTimeOffset CreatedAt);
