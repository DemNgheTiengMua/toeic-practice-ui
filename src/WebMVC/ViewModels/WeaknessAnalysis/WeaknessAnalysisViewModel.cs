using Contracts;

namespace WebMVC.Models;

/// <summary>
/// #243 / D3: the weakness page's prepared state. Topic rows and the lesson href are built by the
/// controller, which is the layer holding the level and modality registries - so the view prints
/// words and a href it was given, and cannot invent a link to another cell.
/// </summary>
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

    /// <summary>Every cell the learner has evidence for, rankable ones first and weakest first.</summary>
    public IReadOnlyList<TopicAccuracyRowViewModel> TopicRows { get; init; } = [];

    /// <summary>The row that may be called the weakest, or null when no cell holds enough evidence.</summary>
    public TopicAccuracyRowViewModel? WeakestTopic { get; init; }
}

/// <summary>#243: one <c>(topic, level)</c> row, already rendered into words, its evidence note and its href.</summary>
public sealed record TopicAccuracyRowViewModel(
    int TopicId,
    string TopicCode,
    string TopicName,
    string LevelLabel,
    string ModalityLabel,
    int Answered,
    int Correct,
    int AccuracyPercent,
    bool IsWeakest,
    bool HasEnoughEvidence,
    /// <summary>
    /// The count, and for a cell below the floor what it still needs. Shown rather than hidden: an
    /// unranked cell that silently vanished would read as "no data" when the learner has some.
    /// </summary>
    string EvidenceNote,
    string? LessonHref);
