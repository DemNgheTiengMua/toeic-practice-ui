namespace Contracts;

public sealed record AdminPaymentEventDto(
    int EventId,
    int OrderId,
    string Source,
    string? ReportedStatus,
    bool SignatureValid,
    bool WasApplied,
    string? Note,
    string? Payload,
    DateTimeOffset CreatedAt);

public sealed record AdminOrderDto(
    int OrderId,
    string OrderCode,
    long PaymentNo,
    int UserId,
    string UserEmail,
    string? UserFullName,
    int PackageId,
    string PackageName,
    int Credits,
    decimal Amount,
    string Status,
    string Gateway,
    string? GatewayTxnRef,
    string? GatewayErrorCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt);

public sealed record AdminOrderDetailDto(
    AdminOrderDto Order,
    IReadOnlyList<AdminPaymentEventDto> Events);

public sealed record AdminOrdersSummaryDto(
    IReadOnlyList<AdminOrderDto> Orders,
    int TotalCount,
    int Page,
    int PageSize,
    int PaidThisMonthCount,
    decimal PaidThisMonthAmount,
    int ReconcilingCount,
    int FailedOrExpiredCount);

public sealed record AdminUserDto(
    int UserId,
    string Email,
    string? FullName,
    string? Phone,
    int CreditsLeft,
    int ExamsTaken,
    int? BestScore,
    DateTimeOffset? LastLoginAt,
    bool IsLocked,
    DateTimeOffset? EmailVerifiedAt,
    DateTimeOffset CreatedAt);

public sealed record AdminUsersSummaryDto(
    IReadOnlyList<AdminUserDto> Users,
    int TotalCount,
    int Page,
    int PageSize,
    int ActiveCount,
    int LockedCount,
    int NotActivatedCount);

public sealed record AdjustCreditsRequest(
    int Delta,
    string Reason);

public sealed record AdjustCreditsResultDto(
    int TxnId,
    int UserId,
    int NewBalance,
    int Delta,
    string Reason);
