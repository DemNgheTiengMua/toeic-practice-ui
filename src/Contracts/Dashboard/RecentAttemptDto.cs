
namespace Contracts;

public sealed record RecentAttemptDto(
    int AttemptId,
    string ExamName,
    ExamPurposeDto Purpose,
    AttemptStatusDto Status,
    DateTimeOffset StartedAt,
    int? TotalScore,
    string? BandCode);
