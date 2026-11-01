using Contracts;

namespace WebMVC.Models;

public sealed record LearningPathIndexViewModel(
    LearningPathSummaryDto Summary,
    string? ErrorMessage = null,
    AiInsightPanelViewModel? InsightPanel = null,
    string? SuccessMessage = null,
    CalibrationViewDto? Calibration = null)
{
    public IReadOnlyList<IGrouping<string, LearningPathModuleSummaryDto>> ModuleGroups { get; } = Summary.Modules.GroupBy(m => m.GroupName ?? "Learning modules").ToArray();
    public string TimeLeft => Summary.RemainingMinutes / 60 > 0
        ? $"{Summary.RemainingMinutes / 60}h {Summary.RemainingMinutes % 60:D2}m left"
        : $"{Summary.RemainingMinutes % 60}m left";
    public int ProgressPercent => Summary.TotalModules > 0 ? (int)Math.Round((double)Summary.CompletedModules / Summary.TotalModules * 100) : 0;

    /// <summary>
    /// #156: the "estimate confidence" line. It prints the count the detector has actually measured,
    /// out of the threshold it applies, and says "estimate" — never "score" or "level", because a
    /// band from a short form is an estimate and the page must not dress it up as a measurement.
    /// </summary>
    public string? CalibrationStatus => Calibration switch
    {
        null => null,
        { PlacedBandCode.Length: 0 } => null,
        { Reason: "not_placed" } => null,
        { Reason: "diverged" or "deferred" } => null,
        { RequiredValidAnswerCount: var required, ValidAnswerCount: var count } when count < required =>
            $"Estimate confidence: still calibrating ({count} of {required} valid answers)",
        _ => "Estimate confidence: checked against your practice",
    };
}
