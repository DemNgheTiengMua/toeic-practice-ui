
namespace Contracts;

/// <summary>#240: the cells a lesson may be authored at. Topics are the #183 canonical twenty, filtered
/// by modality so the editor can never offer a listening topic to a reading lesson.</summary>
public sealed record AdminLessonTopicOptionDto(
    int TopicId,
    string Modality,
    string Code,
    string Name);

public sealed record AdminLessonFormOptionsDto(
    IReadOnlyList<AdminCertificateOptionDto> Certificates,
    IReadOnlyList<AdminLessonSkillOptionDto> Skills,
    IReadOnlyList<AdminLessonCategoryOptionDto> Categories,
    IReadOnlyList<AdminLessonTopicOptionDto>? Topics = null);
