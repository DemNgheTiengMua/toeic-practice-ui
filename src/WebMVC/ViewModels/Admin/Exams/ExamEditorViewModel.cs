namespace WebMVC.Models;

public sealed class ExamEditorViewModel(ExamFormViewModel form)
{
    public IReadOnlyDictionary<int, string> CertificateLabels { get; } = form.Certificates.ToDictionary(c => c.CertId, c => $"{c.Name} ({c.Code.ToUpperInvariant()})");
    public ExamFormViewModel Form { get; } = form;
    public bool IsEdit { get; } = form.ExamId is > 0;
    public ExamEditorPresentation Presentation { get; } = new(form.Detail);
}
