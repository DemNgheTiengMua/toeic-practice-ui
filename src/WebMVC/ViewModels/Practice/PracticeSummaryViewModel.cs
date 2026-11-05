using Contracts;

namespace WebMVC.Models;

public sealed record PracticeSummaryViewModel(PracticeSummaryDto Summary, string StartedAtDisplay)
{
    public AiInsightPanelViewModel InsightPanel { get; init; } = new("practice_session", Summary.SessionId);

    /// <summary>#156: the calibration proposal, when the drill just completed the evidence for one.</summary>
    public CalibrationViewDto? Calibration { get; init; }

    /// <summary>
    /// #187: keyed by display order, because a pool item has no question id. A legacy outcome still
    /// carries its question id, but the order is the only key both shapes share and the summary
    /// already renders in that order.
    /// </summary>
    public IReadOnlyDictionary<int, ExamAudioPlayerModel> QuestionAudio { get; } = Summary.Questions.ToDictionary(
        q => q.DisplayOrder, q => new ExamAudioPlayerModel(q.AudioPath ?? "", AllowReplay: true,
            SectionTitle: "Question audio", StartMs: q.AudioStartMs, EndMs: q.AudioEndMs, Mode: "study"));
}
