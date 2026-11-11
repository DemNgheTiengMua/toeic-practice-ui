namespace Contracts;

/// <summary>#159 / #271: a topic at a level inside a skill card. Accuracy is measured from the same
/// view the weakness analysis reads, so the card never disagrees with that page.</summary>
public sealed record SkillCategoryDto(
    string Code,
    string Name,
    int AccuracyPercent,
    int Answered,
    /// <summary>The cell's authored level, so two rows of one topic at different levels are
    /// distinguishable on the card.</summary>
    string LevelCode);
