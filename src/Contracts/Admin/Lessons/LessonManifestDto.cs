
namespace Contracts;

public sealed record LessonManifestDto(
    IReadOnlyList<LessonManifestItemDto> Lessons);
