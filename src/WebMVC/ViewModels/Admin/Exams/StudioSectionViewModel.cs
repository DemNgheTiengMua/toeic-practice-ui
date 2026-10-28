using Contracts;
namespace WebMVC.Models;

public sealed class StudioSectionViewModel(ExamStudioSectionDto section)
{
    public string ExpectedCount { get; } = section.ExpectedQuestionCount?.ToString() ?? "—";
    public string ExpectedPlacard { get; } = section.ExpectedQuestionCount?.ToString() ?? "Flexible";
    public string SkillKey { get; } = section.SkillId > 0 ? section.SkillId.ToString() : section.Modality.ToString();
    public bool IsComplete { get; } = section.ExpectedQuestionCount.HasValue && section.CurrentQuestionCount == section.ExpectedQuestionCount.Value;
    public string Indicator { get; } = section.ExpectedQuestionCount.HasValue && section.CurrentQuestionCount == section.ExpectedQuestionCount.Value
        ? "[ĐỦ]" : section.ExpectedQuestionCount.HasValue && section.CurrentQuestionCount > section.ExpectedQuestionCount.Value ? "!" : "";
    public bool IsSplit { get; } = section.RequirePassage || section.LayoutType == "SPLIT_HORIZONTAL";
    public bool HasAudioStimulus { get; } = section.RequireAudio || section.Modality == ModalityDto.Listening || section.ClusterSize > 1 || !string.IsNullOrWhiteSpace(section.MasterAudioPath);
    public string? MasterAudioUrl { get; } = MediaUrlHelper.NormalizeMediaUrl(section.MasterAudioPath);
    public string BadgeStyle => IsComplete ? "background: #e6f1ea; color: #0a6b3c;" : "background: #fef3d6; color: #b58900;";
}
