
namespace Contracts;

/// <summary>
/// The detail screen's payload. <paramref name="GrantedCredits"/> and <paramref name="CreditTxnId"/>
/// are null until approval, which is the difference the detail page renders.
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
    int? GrantedCredits);
