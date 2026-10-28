using Contracts;

namespace WebMVC.Models;

public sealed class WizardSkillViewModel
{

    public int? SkillId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ModalityDto Modality { get; set; } = ModalityDto.Listening;
    public int DisplayOrder { get; set; } = 1;
    public int? DurationMinutes { get; set; }
    public List<WizardSectionViewModel> Sections { get; set; } = [];
}
