
namespace Contracts;

/// <summary>One row on <c>/admin/refunds</c>. It carries the learner's identity so accounting can
/// identify the person without a second lookup (spec story 9).</summary>
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
    DateTimeOffset? DecidedAt);
