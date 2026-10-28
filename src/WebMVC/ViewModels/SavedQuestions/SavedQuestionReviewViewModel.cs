using Contracts;

namespace WebMVC.Models;

public sealed record SavedQuestionReviewViewModel(
    SavedQuestionItemDto Question,
    string? ErrorMessage = null)
{
    public ExamAudioPlayerModel Audio { get; } = new(Question.AudioPath ?? "", AllowReplay: true,
        SectionTitle: "Saved Question Audio", BadgeText: "READY", Mode: "study");
    public string SavedAtDisplay { get; init; } = "";
}
