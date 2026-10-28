using Contracts;
namespace WebMVC.Models;
public sealed record CertificateCatalogViewModel(IReadOnlyList<CertificateDto> Certificates, bool IsSignedIn, bool IsAdmin)
{
    public IReadOnlyDictionary<(string Certificate, string Skill), int> QuestionCounts { get; } = Certificates.SelectMany(c => c.Skills.Select(s => new { Certificate = c.Code, Skill = s }))
        .ToDictionary(item => (item.Certificate, item.Skill.Code), item => item.Skill.Sections.Sum(section => section.ExpectedQuestionCount ?? 0));
}
