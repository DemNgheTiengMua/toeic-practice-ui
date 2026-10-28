
namespace Contracts;

public sealed record ComplaintEligibleAttemptDto(
    int AttemptId,
    string ExamTitle,
    DateTimeOffset StartedAt,
    string Status,
    int? TotalScore);
