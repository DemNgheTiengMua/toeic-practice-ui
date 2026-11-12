namespace Contracts;

/// <summary>
/// The detail screen's payload. <paramref name="GrantedCredits"/> and <paramref name="CreditTxnId"/>
/// are null until a credit refund is approved, which is the difference the detail page renders.
/// <paramref name="Amount"/> is the money a money refund returns, and
/// <paramref name="SettledByUserId"/>/<paramref name="SettledAt"/> stay null until an accountant
/// records that the transfer happened (#282).
/// </summary>
public sealed record AdminRefundDetailDto(
    int RefundId,
    int UserId,
    string LearnerName,
    string LearnerEmail,
    RefundMethodDto Method,
    RefundStatusDto Status,
    int CreditCount,
    string Reason,
    int? OrderId,
    string? OrderCode,
    int? ComplaintId,
    DateTimeOffset CreatedAt,
    int RequestedByUserId,
    string? RequestedByUserName,
    int? DecidedByUserId,
    string? DecidedByUserName,
    DateTimeOffset? DecidedAt,
    int? CreditTxnId,
    int? GrantedCredits,
    decimal Amount = 0m,
    decimal? OrderPaidAmount = null,
    int? SettledByUserId = null,
    string? SettledByUserName = null,
    DateTimeOffset? SettledAt = null);
