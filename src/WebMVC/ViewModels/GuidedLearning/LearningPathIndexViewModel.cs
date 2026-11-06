using Contracts;

namespace WebMVC.Models;

public sealed record LearningPathIndexViewModel(
    LearningPathSummaryDto Summary,
    string? ErrorMessage = null,
    AiInsightPanelViewModel? InsightPanel = null,
    string? SuccessMessage = null,
    CalibrationViewDto? Calibration = null)
{
    public string TimeLeft => Summary.RemainingMinutes / 60 > 0
        ? $"{Summary.RemainingMinutes / 60}h {Summary.RemainingMinutes % 60:D2}m left"
        : $"{Summary.RemainingMinutes % 60}m left";
    public int ProgressPercent => Summary.TotalSteps > 0
        ? (int)Math.Round((double)Summary.CompletedSteps / Summary.TotalSteps * 100)
        : 0;

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

    /// <summary>D21a: the step's size, stated before it starts — "8 items · ~12 min". A step with no
    /// count (a lesson, a mock section) states its minutes alone rather than a fictional item count.</summary>
    public static string SizeLabel(PathStepDto step) =>
        step.PlannedItemCount is { } count && step.EstimatedMinutes is { } minutes
            ? $"{count} {ItemNoun(step)} · ~{minutes} min"
            : step.EstimatedMinutes is { } only
                ? $"~{only} min"
                : "—";

    /// <summary>The noun a step's count is counted in. A productive task is counted in tasks, because
    /// "5 items" for a speaking prompt is a number with no meaning.</summary>
    public static string ItemNoun(PathStepDto step) => step.Modality switch
    {
        ModalityDto.Speaking or ModalityDto.Writing => step.PlannedItemCount == 1 ? "task" : "tasks",
        _ => step.PlannedItemCount == 1 ? "item" : "items",
    };

    /// <summary>D19: the step type in words, from the one registry, so no raw code reaches a page.</summary>
    public static string StepTypeLabel(PathStepTypeDto stepType) => stepType switch
    {
        PathStepTypeDto.DiagnosticDrill => "Diagnostic drill",
        PathStepTypeDto.FocusPractice => "Focus practice",
        PathStepTypeDto.ReviewSpaced => "Spaced review",
        PathStepTypeDto.ConceptLesson => "Lesson",
        PathStepTypeDto.ExpandedRemedial => "Lesson + remedial drill",
        PathStepTypeDto.MockSection => "Mock section",
        _ => "Step",
    };
}
