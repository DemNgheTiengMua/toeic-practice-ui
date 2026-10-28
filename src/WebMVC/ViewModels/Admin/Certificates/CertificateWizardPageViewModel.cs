namespace WebMVC.Models;

public sealed class CertificateWizardPageViewModel
{
    public CertificateWizardViewModel Form { get; }
    public bool IsEdit { get; }
    public IReadOnlyList<int> SkillPreviewMaximums { get; }
    public IReadOnlyList<int> SkillPreviewSamples { get; }
    public IReadOnlyList<IReadOnlyList<int>> SectionPreviewSamples { get; }

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
