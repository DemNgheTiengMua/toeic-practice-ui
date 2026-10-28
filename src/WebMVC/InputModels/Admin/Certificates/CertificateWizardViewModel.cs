using Contracts;

namespace WebMVC.Models;

public sealed class CertificateWizardViewModel
{
    public int? CertId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ScoreAggregationRule { get; set; } = "sum";
    public string? PresetCode { get; set; }
    public bool IsActive { get; set; } = true;
    public int Step { get; set; } = 1;

    public IReadOnlyList<CertificateBlueprintPresetDto> Presets { get; set; } = [];
    public List<WizardSkillViewModel> Skills { get; set; } = [];

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
}
