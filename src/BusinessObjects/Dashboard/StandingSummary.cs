namespace BusinessObjects;

/// <summary>
/// #241 / LRN-11, LRN-13: the whole examined vector as the dashboard prints it, read from #211's
/// <c>LearnerStanding</c> and never recomputed here.
/// <para>
/// <see cref="HasNoMeasurement"/> is LRN-13's switch: with nothing measured the dashboard is one
/// block with one action and a sentence about placement, not three blocks of empty figures.
/// </para>
/// <para>
/// <see cref="Overall"/> is the weakest measured skill and is null when nothing is measured. It is
/// not a stored column (#211), so this record carries it rather than deriving it again:
/// <see cref="LimitingSkillName"/> travels with it, because an overall with no name lets a learner
/// read a low number and guess which skill is holding them back.
/// </para>
/// </summary>
public sealed record StandingSummary(
    CefrLevel? Overall,
    string? LimitingSkillCode,
    string? LimitingSkillName,
    bool HasNoMeasurement)
{
    public static StandingSummary None { get; } = new(null, null, null, true);
}
