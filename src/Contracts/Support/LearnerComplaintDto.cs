
namespace Contracts;

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
