
namespace Contracts;

public sealed record MostAttemptedExamDto(
    int ExamId,
    string ExamName,
    int AttemptsCount,
    decimal? AverageScore);
