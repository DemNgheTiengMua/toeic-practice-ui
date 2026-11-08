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
    string? CefrLevelCode = null)
{
    /// <summary>
    /// #235: a pool drill holding fewer items than a session asks for is short. Derived on both sides of
    /// the wire from the same two facts, so the page cannot claim a size the draw did not deliver.
    /// </summary>
    public bool IsShort => SectionId is null && TopicId is not null && QuestionCount < 10;
}
