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

    // E1.2 (#147): the wizard owns the scoring configuration, because a certificate with no scale
    // or no bands cannot be scored and fails every placement submission.
    public int? MinScaledScore { get; set; }
    public int? MaxScaledScore { get; set; }
    public int? ScoreStep { get; set; }
    public List<WizardBandViewModel> Bands { get; set; } = [];
    public List<WizardCriterionViewModel> Criteria { get; set; } = [];

    public IReadOnlyList<CertificateBlueprintPresetDto> Presets { get; set; } = [];
    public List<WizardSkillViewModel> Skills { get; set; } = [];

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
}

/// <summary>One score band as the admin types it. Bands must tile [min, max] with no gap.</summary>
public sealed class WizardBandViewModel
{
    public int? BandId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int MinTotal { get; set; }
    public int MaxTotal { get; set; }
    public int DisplayOrder { get; set; }
}

/// <summary>One rubric criterion. The description is what the grader scores against.</summary>
public sealed class WizardCriterionViewModel
{
    public int? CriterionId { get; set; }
    public string SkillCode { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal MaxScore { get; set; } = 5m;
    public decimal Weight { get; set; } = 1m;
    public int DisplayOrder { get; set; }
    public string? RubricDescription { get; set; }
}
