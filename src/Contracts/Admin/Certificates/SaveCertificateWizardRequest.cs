
namespace Contracts;

/// <summary>Request to create or update a certificate via the linear wizard.</summary>
public sealed record SaveCertificateWizardRequest(
    int? CertId,
    string Code,
    string Name,
    string? Description,
    string ScoreAggregationRule,
    string? PresetCode,
    bool IsActive,
    IReadOnlyList<SaveSkillWizardDto> Skills);
