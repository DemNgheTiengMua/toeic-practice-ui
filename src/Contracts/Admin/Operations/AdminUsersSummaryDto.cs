
namespace Contracts;

public sealed record AdminUsersSummaryDto(
    IReadOnlyList<AdminUserDto> Users,
    int TotalCount,
    int Page,
    int PageSize,
    int ActiveCount,
    int LockedCount,
    int NotActivatedCount);
