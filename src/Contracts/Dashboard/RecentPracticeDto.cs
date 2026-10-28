
namespace Contracts;

public sealed record RecentPracticeDto(
    int SessionId,
    string SectionName,
    PracticeStatusDto Status,
    int CorrectCount,
    int QuestionCount,
    DateTimeOffset StartedAt);
