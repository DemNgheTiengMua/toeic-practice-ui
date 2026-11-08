namespace BusinessObjects;

/// <summary>#159: one card per skill of the active certificate. Built from <c>Cert.Skills</c>, so a
/// certificate that names its skills anything at all still gets a card per skill (#149's rule).
/// <para>
/// #241: <see cref="Standing"/> is the row's measured CEFR state, read from #211's stored table by
/// <c>SkillStateWriter</c>. It is null for a skill nothing has measured - never a fabricated level -
/// and the card list is the certificate's own so a two-skill certificate renders two rows.
/// </para></summary>
public sealed record SkillCard(
    int SkillId,
    string Code,
    string Name,
    Modality Modality,
    int? LatestScaledScore,
    int? MaxScaledScore,
    IReadOnlyList<SkillCategoryAccuracy> WeakestCategories,
    int ScoreDisplayFactor = 1,
    SkillStandingLevel? Standing = null);

/// <summary>#159: a category inside a skill card, measured by the same view the weakness analysis
/// page reads, so the two screens cannot disagree.</summary>
public sealed record SkillCategoryAccuracy(string Code, string Name, int AccuracyPercent, int Answered);
