namespace Contracts;

/// <summary>#155: <paramref name="CategoriesInCooldown"/> and <paramref name="NextReviewDueAt"/> are what
/// the catalog shows while a section is resting: how many of its answered categories are held back, and
/// when the first of them returns.</summary>
public sealed record PracticeSectionDto(
    int SectionId,
    string SkillCode,
    string SkillName,
    ModalityDto Modality,
    string Code,
    string Name,
    int? OptionCount,
    int ExpectedQuestionCount,
    int AvailableQuestionCount,
    int? AccuracyPercentage,
    int AnsweredCount,
    int DisplayOrder,
    int TotalCategoryCount = 0,
    int CategoriesInCooldown = 0,
    DateTimeOffset? NextReviewDueAt = null,
    bool IsUnlocked = false);
