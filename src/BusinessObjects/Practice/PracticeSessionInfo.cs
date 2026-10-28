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
    FeedbackMode FeedbackMode,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);
