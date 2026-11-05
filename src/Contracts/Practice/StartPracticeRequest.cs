namespace Contracts;

/// <summary>
/// #155: a drill takes a section, nothing else. The question count is fixed at ten by the mastery
/// planner and the source is always mastery state, so neither is a field any more.
/// <para>
/// #187: or it takes a cell — a topic and a level — for a drill drawn from the pool. Exactly one of
/// <see cref="SectionId"/> and (<see cref="TopicId"/>, <see cref="CefrLevelCode"/>) is supplied.
/// </para>
/// <para>
/// #190: <see cref="ContentTier"/> says which tier of stock the drill draws. Omitted, it draws
/// production; a demo learner's tier is #245's account-level flag, not a per-request choice, so this
/// field exists for the service's own tests and for whoever wires that flag. It is a string rather
/// than a <c>CefrLevelDto</c>-style enum because the column is a string and a bad value must be a 400
/// with a sentence, not a deserialisation failure.
/// </para>
/// </summary>
public sealed record StartPracticeRequest(
    int? SectionId = null,
    int? TopicId = null,
    string? CefrLevelCode = null,
    string? ContentTier = null);
