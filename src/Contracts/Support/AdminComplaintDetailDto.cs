
namespace Contracts;

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
