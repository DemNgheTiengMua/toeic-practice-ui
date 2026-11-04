using Contracts;

namespace WebMVC.Models;

/// <summary>#161: the landing page used to name TOEIC, IELTS and VSTEP in its copy, its chips and its
/// three cards. It renders whatever the catalogue holds now, so a fourth certificate appears without a
/// template edit and a renamed one is never called by its old name.</summary>
public sealed record HomeViewModel(bool IsSignedIn, bool IsStaff, IReadOnlyList<CertificateDto> Catalog)
{
    public IReadOnlyList<HomeCertificateCard> Certificates { get; } = Catalog.Select(HomeCertificateCard.From).ToList();

    /// <summary>The sample dashboard in the hero, built from the first certificate. Null when the
    /// catalogue is empty, so the page draws no preview rather than inventing one to fill it.</summary>
    public HomePreview? Preview { get; } = Catalog.Count > 0 ? HomePreview.From(Catalog[0]) : null;

    /// <summary>The meta description names the certificates actually on offer, because a description
    /// listing a retired certificate is the same defect one level up from the page body.</summary>
    public string Description => Catalog.Count == 0
        ? "Practice for an English certificate with a free placement exam, a personalized learning path, and skill-based exercises."
        : $"Practice for {string.Join(", ", Certificates.Select(c => c.Name))} with a free placement exam, "
          + "a personalized learning path, and skill-based exercises.";
}

/// <summary>One certificate as the landing page shows it. Every field is read from the catalogue, so a
/// certificate that calls its skills Comprehension and Expression is listed that way.</summary>
public sealed record HomeCertificateCard(string Code, string Name, string SkillsSummary, string RangeSummary)
{
    public static HomeCertificateCard From(CertificateDto cert) => new(
        cert.Code,
        cert.Name,
        cert.Skills.Count == 0 ? "No skills configured yet" : string.Join(" · ", cert.Skills.Select(s => s.Name)),
        cert.Bands.Count == 0 ? "" : $"{cert.Bands.Min(b => b.MinTotal)}–{cert.Bands.Max(b => b.MaxTotal)}");
}

/// <summary>The hero's sample learner dashboard, computed from the first certificate rather than
/// written into the markup: the target is that certificate's own score range and the next step is a
/// section it really has, so a 0-500 scale is not illustrated with a 750.</summary>
public sealed record HomePreview(string CertificateName, string TargetScore, string NextStepTitle, string NextStepMeta)
{
    public static HomePreview From(CertificateDto cert)
    {
        var card = HomeCertificateCard.From(cert);
        var skill = cert.Skills.FirstOrDefault();
        var section = skill?.Sections.FirstOrDefault();
        return new HomePreview(
            card.Name,
            card.RangeSummary.Length > 0 ? card.RangeSummary : "—",
            section is null ? $"{skill?.Name ?? "Practice"} practice" : $"{skill!.Name} — {section.Name}",
            section is null ? "from this certificate" : $"{section.ExpectedQuestionCount} questions");
    }
}
