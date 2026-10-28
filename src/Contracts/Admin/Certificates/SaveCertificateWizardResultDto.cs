
namespace Contracts;

public sealed record SaveCertificateWizardResultDto(
    int CertId,
    string Code,
    string Name,
    int SkillsCount,
    int SectionsCount);
