
namespace Contracts;

public sealed record LessonDetailDto(
    int LessonId,
    string Title,
    string? Summary,
    int SkillId,
    int? CategoryId,
    int EstimatedMinutes,
    List<LessonCardDto> Cards
);
