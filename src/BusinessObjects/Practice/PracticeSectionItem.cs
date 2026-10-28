using System.ComponentModel.DataAnnotations;

namespace BusinessObjects;

/// <summary>One section available for practice under the learner's active certificate.</summary>
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
    int DisplayOrder);
