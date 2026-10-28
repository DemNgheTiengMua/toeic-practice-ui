using System.ComponentModel.DataAnnotations;

namespace Contracts;

public sealed record MentorSkillOptionDto(
    int SkillId,
    int CertId,
    string Code,
    string Name,
    string Modality);
