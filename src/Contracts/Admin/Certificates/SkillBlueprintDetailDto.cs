
namespace Contracts;

public sealed record SkillBlueprintDetailDto(
    int SkillId,
    int CertId,
    string Code,
    string Name,
    ModalityDto Modality,
    int DisplayOrder,
    int? DurationMinutes,
    IReadOnlyList<SectionBlueprintDetailDto> Sections);
