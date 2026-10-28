
namespace BusinessObjects;

public sealed record RecentPractice(
    int SessionId,
    string SectionName,
    PracticeStatus Status,
    int CorrectCount,
    int QuestionCount,
    DateTimeOffset StartedAt);
