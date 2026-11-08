namespace Contracts;

/// <summary>#159: one card per skill of the certificate, named by the skill. The list comes from
/// <c>Cert.Skills</c> (#149's rule), never from a fixed Listening/Reading pair.
/// <para>
/// #241: <paramref name="LevelCode"/>, <paramref name="PositionInBand"/> and
/// <paramref name="MeasuredAt"/> are #211's stored per-skill state, passed through untouched.
/// <b>Every one of them is nullable, and all three are null for a skill nothing has measured.</b>
/// A level that defaulted to "A1" would render a fabricated bottom band - the defect #211 exists to
/// prevent - so absence is expressed by null and never by a placeholder level.
/// </para></summary>
public sealed record SkillCardDto(
    int SkillId,
    string Code,
    string Name,
    ModalityDto Modality,
    int? LatestScaledScore,
    int? MaxScaledScore,
    IReadOnlyList<SkillCategoryDto> WeakestCategories,
    int ScoreDisplayFactor = 1,
    string? LevelCode = null,
    int? PositionInBand = null,
    DateTimeOffset? MeasuredAt = null,
    bool IsStale = false);
