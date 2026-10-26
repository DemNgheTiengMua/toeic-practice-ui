namespace BusinessObjects;

public sealed record MockExamSectionInfo(
    int SectionId,
    string SkillCode,
    string SkillName,
    Modality Modality,
    string SectionCode,
    string SectionName,
    int? ExpectedQuestionCount,
    int QuestionCount,
    int? DurationMinutes,
    int DisplayOrder);
