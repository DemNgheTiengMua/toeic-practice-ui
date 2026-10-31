
namespace Contracts;

/// <summary>E1.2 (#147): one grading criterion authored in the certificate wizard, identified by
/// the skill's code because the skill may not have an id yet on first save.</summary>
public sealed record SaveGradingCriterionWizardDto(
    int? CriterionId,
    string SkillCode,
    string Code,
    string Name,
    decimal MaxScore,
    decimal Weight,
    int DisplayOrder,
    string? RubricDescription);
