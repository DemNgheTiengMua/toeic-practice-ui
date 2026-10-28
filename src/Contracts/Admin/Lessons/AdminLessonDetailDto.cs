
namespace Contracts;

public sealed record AdminLessonDetailDto(
    int LessonId,
    int CertId,
    string CertCode,
    int SkillId,
    string SkillCode,
    int? CategoryId,
    string? CategoryCode,
    string Title,
    string? Summary,
    int EstimatedMinutes,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    IReadOnlyList<LessonCardDto> Cards);
