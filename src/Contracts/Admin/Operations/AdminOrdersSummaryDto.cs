
namespace Contracts;

public sealed record AdminOrdersSummaryDto(
    IReadOnlyList<AdminOrderDto> Orders,
    int TotalCount,
    int Page,
    int PageSize,
    int PaidThisMonthCount,
    decimal PaidThisMonthAmount,
    int ReconcilingCount,
    int FailedOrExpiredCount);
