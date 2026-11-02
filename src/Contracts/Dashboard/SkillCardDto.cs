namespace Contracts;

/// <summary>#159: one card per skill of the certificate, named by the skill. The list comes from
/// <c>Cert.Skills</c> (#149's rule), never from a fixed Listening/Reading pair.</summary>
public sealed record SkillCardDto(
    int SkillId,
    string Code,
    string Name,
    ModalityDto Modality,
    int? LatestScaledScore,
    int? MaxScaledScore,
    IReadOnlyList<SkillCategoryDto> WeakestCategories);
