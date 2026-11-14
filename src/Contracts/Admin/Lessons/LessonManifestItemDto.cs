
namespace Contracts;

/// <summary>
/// #240: the cell the lesson is addressed by travels with the manifest, because a lesson without one
/// is not authorable. <c>TopicCode</c> rather than an id, so a manifest survives a reseeded database
/// the way <c>Skill</c> and <c>CategoryCode</c> already do.
/// </summary>
public sealed record LessonManifestItemDto(
    string Title,
    string Skill,
    string? CategoryCode,
    string? Summary,
    int EstimatedMinutes,
    int DisplayOrder,
    IReadOnlyList<LessonManifestCardDto> Cards,
    string Modality = "reading",
    string? TopicCode = null,
    string CefrLevel = "B1",
    string TargetBands = "ALL");
