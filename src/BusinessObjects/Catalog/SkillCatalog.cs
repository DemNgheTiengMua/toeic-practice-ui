
namespace BusinessObjects;

public sealed record SkillCatalog(
    string Code,
    string Name,
    Modality Modality,
    int? DurationMinutes,
    IReadOnlyList<SectionCatalog> Sections,
    int SkillId = 0,
    int DisplayOrder = 1);
