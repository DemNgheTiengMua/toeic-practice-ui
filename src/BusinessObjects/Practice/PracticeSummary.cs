using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

public sealed record PracticeSummary(
    int SessionId,
    string SectionCode,
    string SectionName,
    Modality Modality,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int TotalQuestions,
    int AnsweredCount,
    int CorrectCount,
    int IncorrectCount,
    int SkippedCount,
    int AccuracyPercentage,
    int HintUsedCount,
    int CorrectWithHintCount,
    int IncorrectWithHintCount,
    IReadOnlyList<QuestionOutcome> Questions);
