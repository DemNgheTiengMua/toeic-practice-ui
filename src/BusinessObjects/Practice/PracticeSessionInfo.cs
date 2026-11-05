using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

/// <summary>
/// #187: a drill's identity. <see cref="SectionId"/> is NULL for a pool drill, which is drawn from
/// <see cref="TopicId"/> + <see cref="CefrLevel"/> instead. <see cref="SectionCode"/> and
/// <see cref="SectionName"/> are the scope's labels either way — the section's for a section drill,
/// the topic's for a pool drill — because every screen prints one label for "where am I".
/// </summary>
public sealed record PracticeSessionInfo(
    int SessionId,
    int? SectionId,
    string SectionCode,
    string SectionName,
    Modality Modality,
    int QuestionCount,
    PracticeStatus Status,
    int CorrectCount,
    QuestionSource QuestionSource,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    int? TopicId = null,
    CefrLevel? CefrLevel = null);
