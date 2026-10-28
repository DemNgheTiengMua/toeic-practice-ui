
namespace Contracts;

public sealed record RubricHighlightDto(
    string SkillName,
    string CriterionName,
    decimal Score,
    decimal MaxScore,
    string? Comment,
    int AttemptId,
    ExamPurposeDto Purpose);
