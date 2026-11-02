namespace Contracts;

/// <summary>#159: a category inside a skill card. Accuracy is measured from the same view the
/// weakness analysis reads, so the card never disagrees with that page.</summary>
public sealed record SkillCategoryDto(
    string Code,
    string Name,
    int AccuracyPercent,
    int Answered);
