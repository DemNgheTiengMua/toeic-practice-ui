
namespace Contracts;

/// <summary>
/// #240: a lesson is addressed by (skill, topic, level), so those four fields are required. The two
/// module codes travel with the card, so one save can author both modules.
/// </summary>
public sealed record AdminLessonUpsertDto(
    int CertId,
    int SkillId,
    int? CategoryId,
    string Title,
    string? Summary,
    int EstimatedMinutes,
    int DisplayOrder,
    IReadOnlyList<AdminLessonCardUpsertDto> Cards,
    string Modality = "reading",
    int TopicId = 0,
    string CefrLevel = "B1");
