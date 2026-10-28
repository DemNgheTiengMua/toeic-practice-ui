using Contracts;

namespace WebMVC.Models;

public sealed record WritingInstructionsViewModel(
    WritingInstructionsDto Instructions,
    string? ErrorMessage = null)
{
    public bool HasCredits { get; } = Instructions.CreditBalance >= Instructions.CreditCost;
}
