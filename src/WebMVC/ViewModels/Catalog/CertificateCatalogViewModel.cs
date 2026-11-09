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

    /// <summary>
    /// A certificate's heading, without saying its code twice.
    /// <para>
    /// The page used to render <c>@cert.Code - @cert.Name</c>, and VSTEP's Name is
    /// "VSTEP - the Vietnamese national test", so the heading read
    /// "VSTEP - VSTEP - the Vietnamese national test". A duplicate is not a styling problem: it is the
    /// page stating the same fact twice, which is the class of drift this rebuild exists to remove. The
    /// datum decides: a name that already opens with its own code keeps its name alone.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, string> Headings { get; } = Certificates
        .ToDictionary(c => c.Code, c => StartsWithCode(c.Code, c.Name)
            ? c.Name
            : $"{c.Code} - {c.Name}");

    private static bool StartsWithCode(string code, string name) =>
        name.StartsWith(code, StringComparison.OrdinalIgnoreCase)
        && (name.Length == code.Length
            || !char.IsLetterOrDigit(name[code.Length]));
}
