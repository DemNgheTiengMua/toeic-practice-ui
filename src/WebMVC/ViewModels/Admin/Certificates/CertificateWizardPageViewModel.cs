namespace WebMVC.Models;

public sealed class CertificateWizardPageViewModel
{
    public CertificateWizardViewModel Form { get; }
    public bool IsEdit { get; }
    public IReadOnlyList<int> SkillPreviewMaximums { get; }
    public IReadOnlyList<int> SkillPreviewSamples { get; }
    public IReadOnlyList<IReadOnlyList<int>> SectionPreviewSamples { get; }

    /// <summary>Set when an existing certificate's blueprint failed to load. The page then offers
    /// this retry link instead of the form, which would otherwise fall back to the default preset
    /// with no CertId, so saving it would create a new certificate.</summary>
    public string? BlueprintRetryUrl { get; init; }

    public CertificateWizardPageViewModel(CertificateWizardViewModel form)
    {
        Form = form;
        IsEdit = form.CertId is > 0;
        SkillPreviewMaximums = form.Skills.Select(s => s.Sections.FirstOrDefault()?.ExpectedQuestionCount ?? 40).ToArray();
        SkillPreviewSamples = SkillPreviewMaximums.Select(maximum => maximum * 3 / 4).ToArray();
        SectionPreviewSamples = form.Skills.Select(s => (IReadOnlyList<int>)s.Sections
            .Select(section => Math.Max(1, (section.ExpectedQuestionCount ?? 10) * 3 / 4)).ToArray()).ToArray();
    }
}
