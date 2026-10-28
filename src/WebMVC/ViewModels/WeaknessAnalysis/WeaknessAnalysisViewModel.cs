using Contracts;

namespace WebMVC.Models;

public sealed class WeaknessAnalysisViewModel(
    WeaknessAnalysisDto data,
    string? errorMessage = null,
    string? successMessage = null)
{
    public AiInsightPanelViewModel InsightPanel { get; init; } = new("dashboard", 0);
    public IReadOnlyDictionary<int, string> SectionBarClasses { get; } = data.Sections.ToDictionary(s => s.SectionId,
        s => s.Answered == 0 ? "" : s.AccuracyPercentage < 70 ? "progress__bar--danger"
            : s.AccuracyPercentage < 80 ? "progress__bar--warning" : "progress__bar--success");
    public WeaknessAnalysisDto Data { get; } = data;
    public string? ErrorMessage { get; } = errorMessage;
    public string? SuccessMessage { get; } = successMessage;
}
