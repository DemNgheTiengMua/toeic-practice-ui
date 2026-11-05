namespace Contracts;

/// <summary>
/// #187: <paramref name="SectionId"/> is NULL for a pool drill, which is drawn from a cell instead;
/// <paramref name="SectionCode"/> and <paramref name="SectionName"/> label the scope either way.
/// </summary>
public sealed record PracticeSessionDto(
    int SessionId,
    int? SectionId,
    string SectionCode,
    string SectionName,
    ModalityDto Modality,
    int QuestionCount,
    PracticeStatusDto Status,
    int CorrectCount,
    QuestionSourceDto QuestionSource,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int? TopicId = null,
    string? CefrLevelCode = null);
