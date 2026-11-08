
namespace Contracts;

public sealed record AdminLessonSummaryDto(
    int LessonId,
    int CertId,
    string CertCode,
    int SkillId,
    string SkillCode,
    string SkillName,
    int? CategoryId,
    string? CategoryCode,
    string? CategoryName,
    string Title,
    string? Summary,
    int EstimatedMinutes,
    int DisplayOrder,
    int CardCount,
    DateTimeOffset CreatedAt,
    int TopicId = 0,
    string TopicCode = "",
    string TopicName = "",
    string CefrLevel = "");
