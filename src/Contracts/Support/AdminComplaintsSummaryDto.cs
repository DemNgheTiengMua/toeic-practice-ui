
namespace Contracts;

public sealed record AdminComplaintsSummaryDto(
    IReadOnlyList<AdminComplaintListItemDto> Complaints,
    int TotalCount,
    int Page,
    int PageSize,
    int PendingCount,
    int ResolvedCount,
    DateTimeOffset? OldestPendingCreatedAt,
    int? OldestPendingId,
    ComplaintTypeDto? OldestPendingType);
