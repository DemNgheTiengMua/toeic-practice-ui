
namespace Contracts;

public sealed record SkillBlueprintPresetDto(
    string Code,
    string Name,
    ModalityDto Modality,
    int DisplayOrder,
    int? DurationMinutes,
    IReadOnlyList<SectionBlueprintPresetDto> Sections);
