namespace Contracts;

public sealed record ComplaintEligibleBookingDto(
    int BookingId,
    string MentorName,
    DateTimeOffset? ScheduledAt,
    string Status);

public sealed record ComplaintEligibleAttemptDto(
    int AttemptId,
    string ExamTitle,
    DateTimeOffset StartedAt,
    string Status,
    int? TotalScore);

public sealed record ComplaintEligibleOrderDto(
    int OrderId,
    string OrderCode,
    string PackageName,
    decimal Amount,
    DateTimeOffset CreatedAt,
    string Status);

public sealed record EligibleSubjectsDto(
    IReadOnlyList<ComplaintEligibleBookingDto> Bookings,
    IReadOnlyList<ComplaintEligibleAttemptDto> Attempts,
    IReadOnlyList<ComplaintEligibleOrderDto> Orders);

public sealed record FileComplaintRequest(
    ComplaintTypeDto Type,
    string Reason,
    int? BookingId = null,
    int? AttemptId = null,
    int? OrderId = null);

/// <summary>
/// Learner view of a filed complaint.
/// Architectural Invariant: InternalNotes is strictly for admins and must NEVER be present here.
/// </summary>
public sealed record LearnerComplaintDto(
    int ComplaintId,
    ComplaintTypeDto Type,
    string Reason,
    ComplaintStatusDto Status,
    string? Resolution,
    DateTimeOffset? ResolvedAt,
    int? ResolvedByGrantTxnId,
    int? GrantedCredits,
    DateTimeOffset CreatedAt,
    int? BookingId,
    int? AttemptId,
    int? OrderId,
    string? SubjectSummary);

public sealed record AdminComplaintListItemDto(
    int ComplaintId,
    int UserId,
    string LearnerName,
    string LearnerEmail,
    ComplaintTypeDto Type,
    string SubjectType,
    int SubjectId,
    string SubjectSummary,
    ComplaintStatusDto Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);

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

public sealed record AdminComplaintDetailDto(
    int ComplaintId,
    int UserId,
    string LearnerName,
    string LearnerEmail,
    ComplaintTypeDto Type,
    string Reason,
    ComplaintStatusDto Status,
    string SubjectType,
    int SubjectId,
    string SubjectSummary,
    string? SubjectDetails,
    DateTimeOffset CreatedAt,
    string? Resolution,
    string? InternalNotes,
    int? ResolvedByUserId,
    string? ResolvedByUserName,
    DateTimeOffset? ResolvedAt,
    int? ResolvedByGrantTxnId,
    int? GrantedCredits);

public sealed record UpdateComplaintStatusRequest(
    ComplaintStatusDto Status);

public sealed record ResolveComplaintRequest(
    string Resolution,
    string InternalNotes,
    int GrantCredits = 0);
