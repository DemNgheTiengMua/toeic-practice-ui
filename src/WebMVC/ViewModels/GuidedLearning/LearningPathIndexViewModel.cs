using Contracts;

namespace WebMVC.Models;

public sealed record LearningPathIndexViewModel(
    LearningPathSummaryDto Summary,
    string? ErrorMessage = null,
    AiInsightPanelViewModel? InsightPanel = null,
    string? SuccessMessage = null)
{
    public IReadOnlyList<IGrouping<string, LearningPathModuleSummaryDto>> ModuleGroups { get; } = Summary.Modules.GroupBy(m => m.GroupName ?? "Learning modules").ToArray();
    public string TimeLeft => Summary.RemainingMinutes / 60 > 0
        ? $"{Summary.RemainingMinutes / 60}h {Summary.RemainingMinutes % 60:D2}m left"
        : $"{Summary.RemainingMinutes % 60}m left";
    public int ProgressPercent => Summary.TotalModules > 0 ? (int)Math.Round((double)Summary.CompletedModules / Summary.TotalModules * 100) : 0;
}
