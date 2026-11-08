namespace BusinessObjects;

/// <summary>
/// #241 / D21a block 3: one examined skill's standing, carrying the CEFR level and the position
/// inside it, or nothing at all.
/// <para>
/// <b>Absence is a shape, not a value.</b> <see cref="Level"/> is nullable and the record is
/// <see cref="IsMeasured"/> only when a level exists, because #211 keeps an unmeasured skill absent
/// from <c>LearnerStanding.Skills</c> rather than present at A1. A record whose level defaulted to
/// <c>CefrLevel.A1</c> would render a fabricated bottom band on every skill the learner has not
/// sat, which is the exact defect #211 exists to prevent.
/// </para>
/// <para>
/// The certificate's own skill list supplies the rows; a skill with no standing is a row that says
/// "not measured". That is different from a skill the certificate does not examine, which has no
/// row at all.
/// </para>
/// </summary>
/// <param name="Level">The measured level, or null when nothing has measured this skill.</param>
/// <param name="PositionInBand">0-100 inside <paramref name="Level"/>. A position, never a percentage.</param>
/// <param name="MeasuredAt">When it was measured, so the row can print its own date.</param>
/// <param name="IsStale">D18's reuse window has closed: the row is still the answer, but it is old.</param>
public sealed record SkillStandingLevel(
    CefrLevel? Level,
    int? PositionInBand,
    DateTimeOffset? MeasuredAt,
    bool IsStale)
{
    public bool IsMeasured => Level is not null;

    /// <summary>Nothing has measured it yet. Deliberately not a level and not a zero.</summary>
    public static SkillStandingLevel NotMeasured { get; } = new(null, null, null, false);
}
