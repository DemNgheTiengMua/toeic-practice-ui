namespace Contracts;

/// <summary>
/// #213: <paramref name="ReuseValidSkills"/> is the learner's choice at the preflight — carry over the
/// skills whose stored result is still inside the 90-day window, or (the default) measure everything.
/// </summary>
public sealed record StartPlacementTestRequest(
    int ExamId,
    bool ReuseValidSkills = false,
    PlacementScopeDto Scope = PlacementScopeDto.AllSkills,
    string? IdempotencyKey = null);
