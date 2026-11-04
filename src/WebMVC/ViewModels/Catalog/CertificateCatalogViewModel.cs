using Contracts;

namespace WebMVC.Models;

/// <summary>The public certificate catalogue, prepared for rendering.</summary>
public sealed record CertificateCatalogViewModel(IReadOnlyList<CertificateDto> Certificates, bool IsSignedIn, bool IsStaff)
{
    public IReadOnlyDictionary<(string Certificate, string Skill), int> QuestionCounts { get; } =
        Certificates.SelectMany(c => c.Skills.Select(s => new { Certificate = c.Code, Skill = s }))
            .ToDictionary(item => (item.Certificate, item.Skill.Code),
                item => item.Skill.Sections.Sum(section => section.ExpectedQuestionCount));

    /// <summary>#161: "Three certificates, one account" was a count written into the copy. The heading
    /// now says whatever number of certificates the catalogue actually holds.</summary>
    public string Heading => Certificates.Count == 1
        ? "One certificate, one account"
        : $"{Certificates.Count} certificates, one account";

    /// <summary>The meta description names the certificates on offer, so a renamed or retired one cannot
    /// be advertised here after it has left the catalogue.</summary>
    public string Description => Certificates.Count == 0
        ? "Every certificate on extra efficient — test structure, score bands and what you can practice for each one."
        : $"{string.Join(", ", Certificates.Select(c => c.Name))} on extra efficient — test structure, "
          + "score bands and what you can practice for each certificate.";

    /// <summary>#161: each certificate's strip, in score order, built here rather than in the markup so
    /// the view only renders what it was handed.</summary>
    public IReadOnlyDictionary<string, BandStripViewModel> BandStrips { get; } = Certificates
        .ToDictionary(c => c.Code, c => BandStripViewModel.From(c.Bands, c.Code, _ => "cefr__band"));
}
