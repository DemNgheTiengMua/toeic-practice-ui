namespace BusinessObjects;

/// <summary>#159: one card per skill of the active certificate. Built from <c>Cert.Skills</c>, so a
/// certificate that names its skills anything at all still gets a card per skill (#149's rule).</summary>
public sealed record SkillCard(
    int SkillId,
    string Code,
    string Name,
    Modality Modality,
    int? LatestScaledScore,
    int? MaxScaledScore,
    IReadOnlyList<SkillCategoryAccuracy> WeakestCategories);

/// <summary>#159: a category inside a skill card, measured by the same view the weakness analysis
/// page reads, so the two screens cannot disagree.</summary>
public sealed record SkillCategoryAccuracy(string Code, string Name, int AccuracyPercent, int Answered);
