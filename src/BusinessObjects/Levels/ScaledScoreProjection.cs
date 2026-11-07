using System;
using System.Collections.Generic;
using System.Linq;

namespace BusinessObjects;

/// <summary>
/// #222: one skill's scaled score read back to the level that score sits in.
/// <para>
/// <b>Every level here is nullable, and that is the point.</b> The cut-score table is a grid of ranges
/// with gaps between them (TOEIC Listening has no row for 56-59), so a score can sit between two bands
/// and the honest answer is "the table does not answer". A non-nullable <see cref="CefrLevel"/> would
/// default to <c>pre_a1</c> - the floor - and the screen would then print a level the certificate never
/// awarded. <see cref="Outcome"/> is what tells a screen which of the four answers this is.
/// </para>
/// </summary>
/// <param name="SkillId">The certificate's skill this row projects.</param>
/// <param name="ScaledScore">The score on the certificate's scale, as the attempt stored it.</param>
/// <param name="AwardedLevel">The level the row awards, or null when it awards none.</param>
/// <param name="PositionInBand">0-100 inside the row's range; null when there is no range.</param>
/// <param name="Outcome">Projected, uncertified, display ceiling, or cannot judge.</param>
/// <param name="CutScoreTableVersion">The version the lookup read; null when there was none.</param>
/// <param name="DisplayCeiling">For a ceiling, the highest value the scale can display.</param>
/// <param name="CannotJudgeReason">Why the table did not answer; null when it did.</param>
public sealed record ScaledScoreSkillProjection(
    int SkillId,
    int ScaledScore,
    CefrLevel? AwardedLevel,
    int? PositionInBand,
    ProjectionOutcome Outcome,
    int? CutScoreTableVersion,
    int? DisplayCeiling = null,
    string? CannotJudgeReason = null)
{
    /// <summary>
    /// True when this skill can move the learner's stored state: a cut row awarded a level and a
    /// position, which is the only combination <see cref="SkillStateWriter"/> may record. An
    /// uncertified range and a display ceiling both refuse to state a level, so they offer nothing to
    /// sync - a sync button on them would write an invention.
    /// </summary>
    public bool CanSync => Outcome == ProjectionOutcome.Projected
        && AwardedLevel is not null
        && PositionInBand is not null;

    /// <summary>
    /// The continuous coordinate D15 stores, derived from the level and the position inside it: A1 is
    /// 1.xx, B1 is 3.xx, C2 is 6.xx. Clamped to the stored axis (1.00-6.99) because C2 at position 100
    /// is 7.00, which the column refuses; the top of the axis is "above C2", so 6.99 is its honest
    /// spelling. The floor sorts below A1 and therefore sits at the bottom of the axis, never at A1's.
    /// </summary>
    public decimal ContinuousCoordinate =>
        AwardedLevel is not { } level
            ? 1.00m
            : level.IsReportableFloor
                ? 1.00m
                : Math.Clamp(Math.Round(level.Rank + (PositionInBand ?? 0) / 100m, 2), 1.00m, 6.99m);
}

/// <summary>
/// #222 / D6 / PRN-03: a mock paper's per-section scaled scores read back against that certificate's
/// own cut-score table. The mirror of <see cref="CertificateProjection"/>: that type goes coordinate to
/// score, this one goes score to level, and both answer "what does the table say" rather than
/// "what can I compute".
/// <para>
/// <b>There is no certificate-level score here, deliberately.</b> The attempt owns its total and its
/// band (single writer, #199/#251); re-summing the skills would be a second total that can disagree
/// with the stored one. And there is no certificate-level level either -
/// <see cref="WeakestProjectedLevel"/> is the weakest <i>measured</i> skill, which is a rule over the
/// skill rows, not a property of the paper.
/// </para>
/// </summary>
/// <param name="Skills">One entry per skill the paper scored.</param>
/// <param name="CutScoreTableVersion">The version the lookup read; null when there was none.</param>
/// <param name="Outcome">Cannot-judge when no skill could be placed.</param>
/// <param name="CannotJudgeReason">Why the table did not answer; null when it did.</param>
public sealed record ScaledScoreProjection(
    IReadOnlyList<ScaledScoreSkillProjection> Skills,
    int? CutScoreTableVersion,
    ProjectionOutcome Outcome,
    string? CannotJudgeReason = null)
{
    /// <summary>The skills that carry a level, in the order they arrived (the certificate's order).</summary>
    public IReadOnlyList<ScaledScoreSkillProjection> LevelBearing =>
        Skills.Where(s => s.AwardedLevel is not null).ToList();

    /// <summary>
    /// The paper's weakest projected skill, by level then by the lowest position, exactly as
    /// <see cref="SkillStateWriter.ReadAsync"/> computes a learner's overall. Copying that rule rather
    /// than re-deriving it is what stops the mock reading "B2" while the dashboard reads "B1" for the
    /// same paper.
    /// <para>
    /// Named <b>weakest projected</b>, not "overall": this is one paper's result, and an unqualified
    /// "overall level" would read as a claim about the learner.
    /// </para>
    /// </summary>
    public CefrLevel? WeakestProjectedLevel => Weakest?.AwardedLevel;

    /// <summary>The skill that produced <see cref="WeakestProjectedLevel"/>, so a screen can name it.</summary>
    public int? WeakestProjectedSkillId => Weakest?.SkillId;

    private ScaledScoreSkillProjection? Weakest => LevelBearing
        .OrderBy(s => s.AwardedLevel!.Value.Rank)
        .ThenBy(s => s.PositionInBand ?? 0)
        .ThenBy(s => s.SkillId)
        .FirstOrDefault();

    /// <summary>
    /// The skills whose projected level differs from the learner's stored state - the sync offer's
    /// subject, and the exact set the sync writes. One implementation, so the offer and the write
    /// cannot disagree about which skills changed.
    /// <para>
    /// A skill with no stored row differs: "nothing measured yet" is a difference, not a match, and
    /// comparing a null standing to a level by ignoring it would hide the one case that most needs the
    /// offer. A skill the table cannot judge never differs - there is nothing to write.
    /// </para>
    /// </summary>
    public IReadOnlyList<int> SkillIdsDifferingFrom(LearnerStanding? stored)
    {
        if (stored is null)
        {
            return LevelBearing.Where(s => s.CanSync).Select(s => s.SkillId).ToList();
        }

        var bySkill = stored.Skills.ToDictionary(s => s.SkillId);
        return Skills
            .Where(s => s.CanSync)
            .Where(s => !bySkill.TryGetValue(s.SkillId, out var state)
                || state.Level != s.AwardedLevel!.Value)
            .Select(s => s.SkillId)
            .ToList();
    }
}
