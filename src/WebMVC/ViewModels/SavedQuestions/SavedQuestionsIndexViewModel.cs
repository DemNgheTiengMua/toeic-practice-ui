using Contracts;

namespace WebMVC.Models;

public sealed record SavedQuestionsIndexViewModel(
    SavedQuestionsViewDto Data,
    string? CurrentSource = null,
    int? CurrentSectionId = null,
    int? CurrentCategoryId = null,
    string? ErrorMessage = null)
{
    public bool HasActiveFilters { get; } = !string.IsNullOrEmpty(CurrentSource) || CurrentSectionId.HasValue || CurrentCategoryId.HasValue;
    public IReadOnlyDictionary<int, ExamAudioPlayerModel> QuestionAudio { get; } = Data.Questions.ToDictionary(
        q => q.QuestionId, q => new ExamAudioPlayerModel(q.AudioPath ?? "", AllowReplay: true,
            SectionTitle: "Saved Question Audio", BadgeText: "READY", Mode: "study"));
    public IReadOnlyDictionary<int, string> SavedAtDisplays { get; init; } = new Dictionary<int, string>();
}
