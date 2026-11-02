using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

public sealed record PracticeSessionInfo(
    int SessionId,
    int SectionId,
    string SectionCode,
    string SectionName,
    Modality Modality,
    int QuestionCount,
    PracticeStatus Status,
    int CorrectCount,
    QuestionSource QuestionSource,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);
