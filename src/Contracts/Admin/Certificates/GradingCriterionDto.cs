
namespace Contracts;

/// <summary>E1.2 (#147): a grading criterion as the wizard authoring form reads it back.</summary>
public sealed record GradingCriterionDto(
    int CriterionId,
    string SkillCode,
    string Code,
    string Name,
    decimal MaxScore,
    decimal Weight,
    int DisplayOrder,
    string? RubricDescription);
