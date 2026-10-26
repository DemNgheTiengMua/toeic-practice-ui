using Contracts;

namespace WebMVC.Models;

public sealed record ExamResultViewModel(
    AttemptResultDto Result,
    string? ErrorMessage = null,
    AiInsightDto? Insight = null)
{
    public string SubmittedAtDisplay { get; init; } = string.Empty;
    public string TotalScoreDisplay { get; init; } = string.Empty;
    public string TotalScoreLabel { get; init; } = string.Empty;
    public string ScoreDeltaDisplay { get; init; } = string.Empty;
    public string DurationDisplay { get; init; } = string.Empty;
    public bool IsGraded { get; init; }
    public bool IsAwaitingGrading { get; init; }
    public int PendingFreeResponses { get; init; }
    public int TotalRawScore { get; init; }
    public int TotalQuestions { get; init; }
    public int AccuracyPercent { get; init; }
    public IReadOnlyList<ExamSkillScoreViewModel> Skills { get; init; } = [];
    public AiInsightPanelViewModel InsightPanel { get; init; } = new("attempt", Result.AttemptId, Insight);
}
