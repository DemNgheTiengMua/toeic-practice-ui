using Contracts;

namespace WebMVC.Models;

public sealed record PracticeSummaryViewModel(PracticeSummaryDto Summary, string StartedAtDisplay)
{
    public AiInsightPanelViewModel InsightPanel { get; init; } = new("practice_session", Summary.SessionId);

    /// <summary>#156: the calibration proposal, when the drill just completed the evidence for one.</summary>
    public CalibrationViewDto? Calibration { get; init; }

    public IReadOnlyDictionary<int, ExamAudioPlayerModel> QuestionAudio { get; } = Summary.Questions.ToDictionary(
        q => q.QuestionId, q => new ExamAudioPlayerModel(q.AudioPath ?? "", AllowReplay: true,
            SectionTitle: "Question audio", StartMs: q.AudioStartMs, EndMs: q.AudioEndMs, Mode: "study"));
}
