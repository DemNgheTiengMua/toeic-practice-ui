using Contracts;

namespace WebMVC.Models;

public sealed record ExamInstructionsViewModel(MockExamInstructionsDto Instructions, string? ErrorMessage = null)
{
    public bool HasCredits { get; } = Instructions.CreditBalance >= Instructions.CreditCost;
    public int CreditsAfterStart { get; } = Instructions.CreditBalance - Instructions.CreditCost;
    public int? QuestionTotal { get; } = Instructions.Sections.Count > 0
        ? Instructions.Sections.Sum(s => s.QuestionCount) : Instructions.QuestionCount;
    public bool NeedsAudioCheck { get; } = Instructions.Sections.Any(s => s.Modality is ModalityDto.Listening or ModalityDto.Speaking);
    public AudioEquipmentCheckModel AudioCheck { get; } = new(
        ShowInput: Instructions.Sections.Any(s => s.Modality == ModalityDto.Speaking),
        ShowOutput: Instructions.Sections.Any(s => s.Modality == ModalityDto.Listening),
        Title: "Audio equipment check");
}
