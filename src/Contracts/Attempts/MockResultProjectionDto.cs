using System.Collections.Generic;

namespace Contracts;

/// <summary>
/// #222 / PRN-03: one skill's scaled score read back to the level the certificate's cut-score table
/// awards for it.
/// <para>
/// <b><see cref="AwardedLevelCode"/> is nullable and must stay nullable.</b> The table has gaps, so a
/// score can sit between two ranges; the honest answer is then
/// <see cref="MockProjectionOutcomeDto.CannotJudge"/> with no level. A default here would render the
/// floor as if the certificate had awarded it.
/// </para>
/// </summary>
/// <param name="SkillId">The certificate's skill; the result's own skill rows carry its name.</param>
/// <param name="ScaledScore">The section score on the certificate's scale.</param>
/// <param name="AwardedLevelCode">The projected level, or null when none was awarded.</param>
/// <param name="PositionInBand">0-100 inside the projected level, or null.</param>
/// <param name="Outcome">Which of the four answers this is.</param>
/// <param name="CannotJudgeReason">The projector's reason code, when it refused.</param>
public sealed record MockSkillProjectionDto(
    int SkillId,
    int ScaledScore,
    string? AwardedLevelCode,
    int? PositionInBand,
    MockProjectionOutcomeDto Outcome,
    string? CannotJudgeReason = null);

/// <summary>
/// #222: <c>ProjectionOutcome</c> on the wire. Four answers, not a boolean: a projected skill has a
/// level, an uncertified one has a score but no level, a ceiling has the scale top and no level, and a
/// cannot-judge has neither.
/// </summary>
public enum MockProjectionOutcomeDto
{
    Projected,
    Uncertified,
    DisplayCeiling,
    CannotJudge,
}

/// <summary>
/// #222: a mock result's certificate score and the separately labelled projected level beside it.
/// <para>
/// <b>The two are never merged.</b> <see cref="CertificateScore"/> is the paper's own total on the
/// certificate's scale and <see cref="BandCode"/> is that certificate's band row - neither is a CEFR
/// level, and the page labels them as what they are. The projected level is per skill and the overall
/// is the weakest measured skill, exactly as the stored state computes it.
/// </para>
/// <para>
/// <b><see cref="SyncOffered"/> is the whole point of the split.</b> Reading this record writes nothing;
/// a learner who wants the paper to count must call the sync endpoint, which is the only writer.
/// </para>
/// </summary>
/// <param name="AttemptId">The mock attempt.</param>
/// <param name="CertId">The certificate whose scale and cut scores were read.</param>
/// <param name="CertificateScore">The attempt's total, in stored units - divide by
/// <paramref name="ScoreDisplayFactor"/> before printing.</param>
/// <param name="BandCode">The certificate's own band for that total; a band, not a level.</param>
/// <param name="BandName">That band's readable name.</param>
/// <param name="ScoreDisplayFactor">Stored units per reported point (#152).</param>
/// <param name="Skills">One entry per skill the paper scored.</param>
/// <param name="WeakestProjectedLevelCode">The lowest projected level among the paper's skills, by the
/// same rule the learner's stored overall uses (weakest measured skill). <b>Named "weakest projected
/// skill" rather than "overall" on purpose</b>: the paper is one sitting, and an unqualified "overall
/// level" would read as a claim about the learner.</param>
/// <param name="WeakestProjectedSkillId">The skill that produced that level, so the page can name it.</param>
/// <param name="StoredOverallLevelCode">The learner's stored overall, for the comparison; null when
/// nothing is measured.</param>
/// <param name="StoredLimitingSkillCode">The stored state's own limiting skill.</param>
/// <param name="HasNoStoredMeasurement">True when the learner has no stored per-skill state yet, so the
/// offer reads "nothing measured yet" rather than "your level changed".</param>
/// <param name="DifferingSkillIds">The skills whose projected level differs from the stored state - the
/// same set the sync writes, produced by one comparison so the offer and the write cannot disagree.</param>
/// <param name="SyncOffered">True only when a syncable skill differs from the stored state.</param>
/// <param name="Historical">True for an attempt that predates every cut-score version; it renders its
/// native score and band and never gains a projected level.</param>
/// <param name="CannotJudgeReason">The projector's reason when the paper could not be projected.</param>
/// <param name="CutScoreTableVersion">The table version the projection read.</param>
public sealed record MockResultProjectionDto(
    int AttemptId,
    int CertId,
    int CertificateScore,
    string? BandCode,
    string? BandName,
    int ScoreDisplayFactor,
    IReadOnlyList<MockSkillProjectionDto> Skills,
    string? WeakestProjectedLevelCode,
    int? WeakestProjectedSkillId,
    string? StoredOverallLevelCode,
    string? StoredLimitingSkillCode,
    bool HasNoStoredMeasurement,
    IReadOnlyList<int> DifferingSkillIds,
    bool SyncOffered,
    bool Historical,
    string? CannotJudgeReason = null,
    int? CutScoreTableVersion = null);

/// <summary>#222: the answer to a sync - the learner's standing afterwards, per skill.</summary>
public sealed record MockSyncResultDto(
    int CertId,
    string? OverallLevelCode,
    int? LimitingSkillId,
    string? LimitingSkillCode,
    IReadOnlyList<MockSyncSkillDto> Skills);

public sealed record MockSyncSkillDto(
    int SkillId,
    string SkillCode,
    string LevelCode,
    int PositionInBand,
    string Source);
