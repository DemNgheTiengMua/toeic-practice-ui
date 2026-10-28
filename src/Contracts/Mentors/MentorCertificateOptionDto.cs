using System.ComponentModel.DataAnnotations;

namespace Contracts;

public sealed record MentorCertificateOptionDto(
    int CertId,
    string Code,
    string Name,
    IReadOnlyList<MentorSkillOptionDto> Skills);
