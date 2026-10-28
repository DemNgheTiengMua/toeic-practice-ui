
namespace BusinessObjects;

public sealed record RubricHighlight(
    string SkillName,
    string CriterionName,
    decimal Score,
    decimal MaxScore,
    string? Comment,
    int AttemptId,
    ExamPurpose Purpose);
