
namespace Contracts;

public sealed record LessonManifestItemDto(
    string Title,
    string Skill,
    string? CategoryCode,
    string? Summary,
    int EstimatedMinutes,
    int DisplayOrder,
    IReadOnlyList<LessonManifestCardDto> Cards);
