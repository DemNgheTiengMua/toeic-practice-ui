using Contracts;

namespace WebMVC.Models;

public sealed record TargetBandViewModel(
    TargetBandViewDto View,
    string? ErrorMessage = null,
    string? SuccessMessage = null)
{
    public int CurrentScore { get; } = View.CurrentTotalScore ?? 0;
    public string TargetCode { get; } = View.TargetBandCode ?? View.AvailableBands.FirstOrDefault(b => !b.IsCurrent)?.Code ?? "B2";
    public int PointsNeeded { get; init; }
    public string NeededDisplay { get; init; } = string.Empty;
    public IReadOnlyList<AttemptBandViewModel> Bands { get; init; } = [];
}
