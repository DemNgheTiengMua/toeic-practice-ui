namespace BusinessObjects;

/// <summary>
/// #210: one skill's projection onto a certificate's own scale — the score, and the level the
/// certificate is willing to award at that score.
/// <para>
/// <see cref="AwardedLevel"/> is null whenever the certificate cannot make the claim:
/// <see cref="ProjectionOutcome.Uncertified"/> (scored but not certified), and
/// <see cref="ProjectionOutcome.DisplayCeiling"/> (the learner is above the highest level the
/// certificate expresses, so only the top of the scale is recorded). A null here is a refusal to
/// state a level, which is the honest answer, not missing data.
/// </para>
/// </summary>
/// <param name="SkillId">The certificate's skill this row projects.</param>
/// <param name="Coordinate">The measured CEFR coordinate the projection read.</param>
/// <param name="AwardedLevel">The level the certificate awards at this score, or null.</param>
/// <param name="ProjectedScore">The score on the certificate's scale; null only when cannot-judge.</param>
/// <param name="Outcome">Which of the four answers this is.</param>
/// <param name="CutScoreTableVersion">The version the projection read, so it can be reproduced. Null
/// only when there was no version to read.</param>
/// <param name="DisplayCeiling">For a ceiling outcome, the highest value the scale can display.</param>
/// <param name="CannotJudgeReason">Why the table did not answer; null when it did.</param>
public sealed record SkillProjection(
    int SkillId,
    CefrLevel Coordinate,
    CefrLevel? AwardedLevel,
    int? ProjectedScore,
    ProjectionOutcome Outcome,
    int? CutScoreTableVersion,
    int? DisplayCeiling = null,
    string? CannotJudgeReason = null);

/// <summary>
/// #210 / PRN-03: the certificate-level projection. <b>It carries no level member, deliberately.</b>
/// A certificate reports a score on its own scale; a level is per skill. The moment this type has a
/// <see cref="CefrLevel"/> the product can label a total with a band again — the exact claim
/// LVL-12 removes ("TOEIC 785–940 is B2") — so the absence is the guard, and
/// <c>CefrProjectionServiceTests</c> asserts it.
/// </summary>
/// <param name="CombinedTotal">The certificate's own aggregate, or null when it reports skills
/// separately (TOEIC S&amp;W) or when any skill could not be judged.</param>
/// <param name="AggregationRule">The rule <see cref="CombinedTotal"/> was produced by.</param>
/// <param name="ReportsSkillsSeparately">True for a certificate that reports no combined total at all.</param>
/// <param name="CutScoreTableVersion">The version that produced this projection, null when there was
/// no table to read.</param>
public sealed record CertificateProjection(
    IReadOnlyList<SkillProjection> Skills,
    int? CombinedTotal,
    string AggregationRule,
    bool ReportsSkillsSeparately,
    int? CutScoreTableVersion,
    ProjectionOutcome Outcome,
    string? CannotJudgeReason = null);
