namespace Contracts;

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
    int DisplayOrder);
