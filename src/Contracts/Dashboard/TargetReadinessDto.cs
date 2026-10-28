
namespace Contracts;

public sealed record TargetReadinessDto(
    string? CurrentBandCode,
    string? CurrentBandName,
    int? CurrentTotalScore,
    string? CurrentSource,
    string? TargetBandCode,
    string? TargetBandName,
    int? TargetMinScore,
    int CompletedPathSteps,
    int TotalPathSteps);
