
namespace Contracts;

public sealed record AdminLessonSkillOptionDto(
    int SkillId,
    int CertId,
    string Code,
    string Name,
    string Modality);
