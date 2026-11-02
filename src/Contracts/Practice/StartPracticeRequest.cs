namespace Contracts;

/// <summary>#155: a drill takes a section, nothing else. The question count is fixed at ten by the
/// mastery planner and the source is always mastery state, so neither is a field any more.</summary>
public sealed record StartPracticeRequest(int SectionId);
