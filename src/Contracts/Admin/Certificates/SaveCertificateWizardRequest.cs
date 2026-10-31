
namespace Contracts;

/// <summary>Request to create or update a certificate via the linear wizard.</summary>
/// <remarks>
/// E1.2 (#147): the wizard owns the whole scoring configuration. A certificate saved here is
/// scoreable immediately — its skills carry the scale, its bands cover the total range, and each
/// graded skill carries its rubric. There is no separate score-conversion screen.
/// </remarks>
public sealed record SaveCertificateWizardRequest(
    int? CertId,
    string Code,
    string Name,
    string? Description,
    string ScoreAggregationRule,
    string? PresetCode,
    bool IsActive,
    IReadOnlyList<SaveSkillWizardDto> Skills,
    int? MinScaledScore = null,
    int? MaxScaledScore = null,
    int? ScoreStep = null,
    IReadOnlyList<SaveScoreBandWizardDto>? Bands = null,
    IReadOnlyList<SaveGradingCriterionWizardDto>? Criteria = null);
