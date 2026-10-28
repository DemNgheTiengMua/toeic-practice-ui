
namespace Contracts;

public sealed record SaveSkillWizardDto(
    int? SkillId,
    string Code,
    string Name,
    ModalityDto Modality,
    int DisplayOrder,
    int? DurationMinutes,
    IReadOnlyList<SaveSectionWizardDto> Sections);
