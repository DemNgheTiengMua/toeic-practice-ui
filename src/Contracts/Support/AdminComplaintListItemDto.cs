
namespace Contracts;

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
