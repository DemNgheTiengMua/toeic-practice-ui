using System.Collections.Generic;

namespace BusinessObjects;

/// <summary>
/// #211 / D15: one learner's standing in one skill, as every screen reads it.
/// <para>
/// This is a read model over the stored row, not a second copy of it: <see cref="Level"/>,
/// <see cref="PositionInBand"/> and <see cref="ContinuousCoordinate"/> are the stored values, and the
/// only thing computed here is <see cref="IsStale"/>, which is one comparison against the clock.
/// </para>
/// </summary>
/// <param name="SkillId">The certificate's skill.</param>
/// <param name="SkillCode">The skill's own code, so a sign can name it without a second lookup.</param>
/// <param name="Level">The measured level.</param>
/// <param name="PositionInBand">0-100 inside the band. A position, never a percentage.</param>
/// <param name="ContinuousCoordinate">The measured coordinate the level was projected from.</param>
/// <param name="Source">What measured it.</param>
/// <param name="MeasuredAt">When it was measured, so a screen can print the date it is standing on.</param>
/// <param name="ExpiresAt">When D18's reuse window closes.</param>
/// <param name="IsStale">True once <see cref="ExpiresAt"/> has passed. The row is still the answer; this
/// is what lets a screen mark it rather than silently present an eight-month-old result as today's.</param>
public sealed record SkillStanding(
    int SkillId,
    string SkillCode,
    CefrLevel Level,
    int PositionInBand,
    decimal ContinuousCoordinate,
    string Source,
    DateTimeOffset MeasuredAt,
    DateTimeOffset ExpiresAt,
    bool IsStale);

/// <summary>
/// #211 / D15 / TRN-13 identity 2: a learner's whole examined vector, with the overall computed.
/// <para>
/// <b>The overall is not a member that can be written; it is produced by <see cref="Overall"/> and
/// <see cref="LimitingSkillId"/> together.</b> It is the weakest MEASURED skill, and the skill that
/// produced it is named beside it - an overall with no name lets the learner read a low number and
/// guess which skill is holding them back. An unmeasured skill is absent from
/// <see cref="Skills"/> rather than present at A1: a fabricated bottom band is the same class of
/// invention as a prorated study-hour estimate.
/// </para>
/// <para>
/// <b>The tie is broken by position, then by the certificate's own order.</b> Two skills at B1 are not
/// equally weak: 5/100 is one good answer from dropping to A2, 80/100 is not. Naming the 80/100 skill as
/// "the limiting skill" would point the learner's study time at the wrong modality. So the weakest is
/// the lowest level, then the lowest position - the rule D5 already applies to a placement's
/// <c>LimitingSkillId</c>, so the two surfaces name the same skill for the same data.
/// <para>
/// Position is not unique on its own (two skills can both sit at 50/100), so the certificate's
/// <c>DisplayOrder</c> then <c>SkillId</c> is the final tie-break - both stored, both unique per
/// certificate. "Whichever row the database returned first" is not deterministic across a plan change
/// or an index addition.
/// </para>
/// </para>
/// </summary>
public sealed record LearnerStanding(
    int CertId,
    IReadOnlyList<SkillStanding> Skills,
    CefrLevel? Overall,
    int? LimitingSkillId,
    string? LimitingSkillCode)
{
    /// <summary>True when nothing has been measured, so the screen owes the learner a placement, not a band.</summary>
    public bool HasNoMeasurement => Skills.Count == 0;
}
