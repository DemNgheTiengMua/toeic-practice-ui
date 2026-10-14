using Contracts;

namespace WebMVC.Models;

public sealed class WeaknessAnalysisViewModel(
    WeaknessAnalysisDto data,
    string? errorMessage = null,
    string? successMessage = null)
{
    public WeaknessAnalysisDto Data { get; } = data;
    public string? ErrorMessage { get; } = errorMessage;
    public string? SuccessMessage { get; } = successMessage;
}
