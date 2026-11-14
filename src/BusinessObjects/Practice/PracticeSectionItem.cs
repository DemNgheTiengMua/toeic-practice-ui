namespace BusinessObjects;

/// <summary>One section available for practice under the learner's active certificate.
/// <para>#155: <paramref name="TotalCategoryCount"/> and <paramref name="CategoriesInCooldown"/> are how the
/// catalog tells the learner a section is being rested rather than empty. Both count categories that
/// have actually been answered (a mastery row exists), so an untested section reads "0 of 0 in
/// cooldown" - which is true, and which keeps the page from claiming a section is resting before
/// anything has been answered in it.</para>
/// <paramref name="NextReviewDueAt"/> is the soonest moment any of the section's categories may be
/// drilled again; null when nothing is resting.</summary>
public sealed record PracticeSectionItem(
    int SectionId,
    string SkillCode,
    string SkillName,
    Modality Modality,
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
