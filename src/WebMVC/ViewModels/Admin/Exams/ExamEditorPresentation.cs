using Contracts;
namespace WebMVC.Models;

public sealed class ExamEditorPresentation(AdminExamDetailDto? detail)
{
    public IReadOnlyList<AdminExamSectionProgressDto> ListeningSections { get; } = detail?.SectionProgress.Where(s => s.Modality == ModalityDto.Listening).ToArray() ?? [];
    public IReadOnlyDictionary<int, AdminExamSectionAudioDto> AudioBySection { get; } = detail?.Audios.ToDictionary(a => a.SectionId) ?? [];
    public IReadOnlyDictionary<int, string> AudioDescriptions { get; } = detail?.Audios.ToDictionary(a => a.SectionId,
        a => $"{a.AudioPath} — {a.DurationSec / 60}:{a.DurationSec % 60:00} — {Math.Round((double)a.SizeBytes / (1024 * 1024), 1)} MB") ?? [];
    public IReadOnlyDictionary<int, string> ExpectedCounts { get; } = detail?.SectionProgress.ToDictionary(s => s.SectionId, s => s.ExpectedQuestionCount?.ToString() ?? "—") ?? [];
    public int UploadedCount { get; } = detail?.Audios.Count ?? 0;
    public int TotalListening => ListeningSections.Count;
    public int Percent => TotalListening > 0 ? (int)Math.Round((double)UploadedCount / TotalListening * 100) : 100;
    public bool AllComplete { get; } = detail?.SectionProgress.All(s => s.IsComplete) ?? false;
    public int ListeningMissing { get; } = detail?.SectionProgress.Count(s => s.Modality == ModalityDto.Listening && !s.HasAudio) ?? 0;
    public int TotalEntered { get; } = detail?.ActualQuestionCount ?? 0;
    public int Required { get; } = detail?.QuestionCount ?? detail?.SectionProgress.Sum(s => s.ExpectedQuestionCount ?? 0) ?? 0;
    public IReadOnlyDictionary<int, string> BadgeClasses { get; } = detail?.SectionProgress.ToDictionary(s => s.SectionId,
        s => s.IsComplete ? "badge badge--success" : "badge badge--warning") ?? [];
}
