
namespace Contracts;

public sealed record AdminLessonUpsertDto(
    int CertId,
    int SkillId,
    int? CategoryId,
    string Title,
    string? Summary,
    int EstimatedMinutes,
    int DisplayOrder,
    IReadOnlyList<AdminLessonCardUpsertDto> Cards);
