using Contracts;
namespace WebMVC.Models;

public sealed class StudioSectionViewModel(ExamStudioSectionDto section)
{
    // #157: ExpectedQuestionCount is NOT NULL, so the "Flexible" / em-dash placeholders are gone.
    // Every section states a number, and completion is a plain comparison.
    public string ExpectedCount { get; } = section.ExpectedQuestionCount.ToString();
    public string ExpectedPlacard { get; } = section.ExpectedQuestionCount.ToString();
    public string SkillKey { get; } = section.SkillId > 0 ? section.SkillId.ToString() : section.Modality.ToString();
    public bool IsComplete { get; } = section.CurrentQuestionCount == section.ExpectedQuestionCount;
    public string Indicator { get; } = section.CurrentQuestionCount == section.ExpectedQuestionCount
        ? "[ĐỦ]" : section.CurrentQuestionCount > section.ExpectedQuestionCount ? "!" : "";
    public bool IsSplit { get; } = section.RequirePassage || section.LayoutType == "SPLIT_HORIZONTAL";
    public bool HasAudioStimulus { get; } = section.RequireAudio || section.Modality == ModalityDto.Listening || section.ClusterSize > 1 || !string.IsNullOrWhiteSpace(section.MasterAudioPath);
    public string? MasterAudioUrl { get; } = MediaUrlHelper.NormalizeMediaUrl(section.MasterAudioPath);
    public string BadgeStyle => IsComplete ? "background: #e6f1ea; color: #0a6b3c;" : "background: #fef3d6; color: #b58900;";
}
