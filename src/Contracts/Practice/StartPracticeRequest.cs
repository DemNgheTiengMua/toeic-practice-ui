namespace Contracts;

/// <summary>
/// #155: a drill takes a section, nothing else. The question count is fixed at ten by the mastery
/// planner and the source is always mastery state, so neither is a field any more.
/// <para>
/// #187: or it takes a cell — a topic and a level — for a drill drawn from the pool. Exactly one of
/// <see cref="SectionId"/> and (<see cref="TopicId"/>, <see cref="CefrLevelCode"/>) is supplied.
/// </para>
/// </summary>
public sealed record StartPracticeRequest(
    int? SectionId = null,
    int? TopicId = null,
    string? CefrLevelCode = null);
