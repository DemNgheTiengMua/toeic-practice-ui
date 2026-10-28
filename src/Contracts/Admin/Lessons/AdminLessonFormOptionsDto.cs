
namespace Contracts;

public sealed record AdminLessonFormOptionsDto(
    IReadOnlyList<AdminCertificateOptionDto> Certificates,
    IReadOnlyList<AdminLessonSkillOptionDto> Skills,
    IReadOnlyList<AdminLessonCategoryOptionDto> Categories);
