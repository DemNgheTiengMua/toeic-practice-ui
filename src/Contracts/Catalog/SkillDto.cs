
namespace Contracts;

public sealed record SkillDto(
    string Code,
    string Name,
    ModalityDto Modality,
    int? DurationMinutes,
    IReadOnlyList<SectionDto> Sections,
    int SkillId = 0,
    int DisplayOrder = 1);
