namespace Contracts;

/// <summary>
/// #243 / D3: one rankable <c>(topic, level)</c> cell of the weakness analysis. The percentage is the
/// SQL view's, and <see cref="LessonId"/> is the lesson for this cell - see
/// <c>BusinessObjects.TopicAccuracy</c> for why both are that way.
/// </summary>
public sealed record TopicAccuracyDto(
    int TopicId,
    string TopicCode,
    string TopicName,
    ModalityDto Modality,
    string LevelCode,
    int Answered,
    int Correct,
    int AccuracyPercent,
    bool IsWeakest,
    /// <summary>PLC-68's <c>n_min</c> for this cell's modality: 12 items or 3 tasks.</summary>
    int ObservationFloor,
    int? LessonId);
