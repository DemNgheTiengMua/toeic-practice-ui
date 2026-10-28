
namespace Contracts;

public sealed record AdminLessonCategoryOptionDto(
    int CategoryId,
    int CertId,
    string Code,
    string Name);
