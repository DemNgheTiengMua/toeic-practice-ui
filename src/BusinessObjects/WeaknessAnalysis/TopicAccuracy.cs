namespace BusinessObjects;

/// <summary>
/// #243 / D3: one rankable <c>(topic, level)</c> cell of the weakness analysis, as the view reported it.
/// <para>
/// <b>The percentage arrives computed.</b> <see cref="AccuracyPercent"/> is the SQL view's arithmetic
/// (D3: "Accuracy is computed per topic and per level by a view, from stored answers, not by
/// application code"); this record carries it, it does not derive it.
/// </para>
/// <para>
/// <b><see cref="LessonId"/> is the lesson for this cell</b>, not for this topic: a lesson is
/// addressed by <c>(skill, topic, level)</c> (#240), so a B1 weakness must not link to the B2 lesson.
/// NULL when no lesson exists for the cell yet, which is a content gap rather than an error - the page
/// omits the link instead of linking somewhere else.
/// </para>
/// </summary>
public sealed record TopicAccuracy(
    int TopicId,
    string TopicCode,
    string TopicName,
    Modality Modality,
    CefrLevel CefrLevel,
    int Answered,
    int Correct,
    int AccuracyPercent,
    bool IsWeakest,
    /// <summary>
    /// PLC-68's <c>n_min</c> for this cell's modality, carried so a page can say how far a cell is
    /// from being rankable without keeping a second copy of the number.
    /// </summary>
    int ObservationFloor,
    int? LessonId);
