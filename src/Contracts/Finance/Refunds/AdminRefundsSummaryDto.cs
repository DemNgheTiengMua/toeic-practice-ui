
namespace Contracts;

/// <summary>The list plus the counts the two stat tiles read, so the screen never derives a figure
/// the service did not state.</summary>
public sealed record AdminRefundsSummaryDto(
    IReadOnlyList<AdminRefundListItemDto> Refunds,
    int TotalCount,
    int Page,
    int PageSize,
    int RequestedCount,
    int ApprovedCount,
    int RejectedCount,
    int CreditsRefundedTotal);
