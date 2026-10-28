namespace Contracts;

public sealed record PracticeSessionDto(
    int SessionId,
    int SectionId,
    string SectionCode,
    string SectionName,
    ModalityDto Modality,
    int QuestionCount,
    PracticeStatusDto Status,
    int CorrectCount,
    QuestionSourceDto QuestionSource,
    FeedbackModeDto FeedbackMode,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);
