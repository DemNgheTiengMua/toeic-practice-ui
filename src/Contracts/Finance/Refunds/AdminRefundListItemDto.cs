namespace Contracts;

/// <summary>One row on <c>/admin/refunds</c>. It carries the learner's identity so accounting can
/// identify the person without a second lookup (spec story 9), and the order code so a money refund
/// can be reconciled against the gateway statement (story 3).</summary>
public sealed record AdminRefundListItemDto(
    int RefundId,
    int UserId,
    string LearnerName,
    string LearnerEmail,
    RefundMethodDto Method,
    RefundStatusDto Status,
    int CreditCount,
    string Reason,
    int? OrderId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    decimal Amount = 0m,
    string? OrderCode = null,
    DateTimeOffset? SettledAt = null);
