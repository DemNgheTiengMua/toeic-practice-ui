namespace Contracts;

public sealed record MockExamSectionInfoDto(
    int SectionId,
    string SkillCode,
    string SkillName,
    ModalityDto Modality,
    string SectionCode,
    string SectionName,
    int? ExpectedQuestionCount,
    int QuestionCount,
    int? DurationMinutes,
    int DisplayOrder);
