using Contracts;

namespace WebMVC.Models;

public sealed record SpeakingInstructionsViewModel(
    SpeakingInstructionsDto Instructions,
    string? ErrorMessage = null)
{
    public bool HasCredits { get; } = Instructions.CreditBalance >= Instructions.CreditCost;
    public AudioEquipmentCheckModel AudioEquipment { get; } = new(ShowInput: true, ShowOutput: true, Title: "Audio equipment check");
}
