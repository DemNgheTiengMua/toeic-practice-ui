
namespace BusinessObjects;

public sealed record RecentAttempt(
    int AttemptId,
    string ExamName,
    ExamPurpose Purpose,
    AttemptStatus Status,
    DateTimeOffset StartedAt,
    int? TotalScore,
    string? BandCode);
