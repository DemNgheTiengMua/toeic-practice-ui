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
    DateTimeOffset CreatedAt);

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

public sealed record AdminLessonCardUpsertDto(
    int? CardId,
    string CardType,
    string Title,
    string ContentMarkdown,
    string? QuestionPrompt = null,
    string? OptionA = null,
    string? OptionB = null,
    string? OptionC = null,
    string? OptionD = null,
    string? CorrectOption = null,
    string? Explanation = null,
    string? Rationale = null,
    int DisplayOrder = 0);

public sealed record AdminLessonUpsertDto(
    int CertId,
    int SkillId,
    int? CategoryId,
    string Title,
    string? Summary,
    int EstimatedMinutes,
    int DisplayOrder,
    IReadOnlyList<AdminLessonCardUpsertDto> Cards);

public sealed record AdminLessonImportResultDto(
    int CreatedCount,
    int UpdatedCount,
    int TotalCardsImported,
    IReadOnlyList<string> Messages);

public sealed record LessonManifestDto(
    IReadOnlyList<LessonManifestItemDto> Lessons);

public sealed record LessonManifestItemDto(
    string Title,
    string Skill,
    string? CategoryCode,
    string? Summary,
    int EstimatedMinutes,
    int DisplayOrder,
    IReadOnlyList<LessonManifestCardDto> Cards);

public sealed record LessonManifestCardDto(
    string Type,
    string Title,
    string ContentMarkdown,
    string? QuestionPrompt = null,
    string? OptionA = null,
    string? OptionB = null,
    string? OptionC = null,
    string? OptionD = null,
    string? CorrectOption = null,
    string? Explanation = null,
    string? Rationale = null);

public sealed record AdminLessonSkillOptionDto(
    int SkillId,
    int CertId,
    string Code,
    string Name,
    string Modality);

public sealed record AdminLessonCategoryOptionDto(
    int CategoryId,
    int CertId,
    string Code,
    string Name);

public sealed record AdminLessonFormOptionsDto(
    IReadOnlyList<AdminCertificateOptionDto> Certificates,
    IReadOnlyList<AdminLessonSkillOptionDto> Skills,
    IReadOnlyList<AdminLessonCategoryOptionDto> Categories);

