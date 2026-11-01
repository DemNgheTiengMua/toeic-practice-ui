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

    /// <summary>#153: the goal already saved, or null when the learner has never set one.</summary>
    public GoalStatusDto? Goal => View.Goal;

    /// <summary>#153: the verdict, flattened for the view's single switch. Null when the goal cannot
    /// be judged — no deadline, or bands that carry no study hours.</summary>
    public FeasibilityStatusDto? Status => View.Goal?.Status;

    public bool HasDeadline => View.Goal?.TargetDate is not null;

    /// <summary>The date the form starts on: the saved deadline, or 30 days out — the spec's own
    /// worked example, not a guess at what the learner can do.</summary>
    public string TargetDateValue => (View.Goal?.TargetDate ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30))
        .ToString("yyyy-MM-dd");

    public string WeeklyHoursValue => (View.Goal?.WeeklyStudyHours ?? 6m).ToString("0.#",
        System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Both numbers the refusal rests on, so the screen never asks to be taken on trust.</summary>
    public string RequiredHoursDisplay => View.Goal?.RequiredHours is { } required ? $"{required:0} h" : "—";
    public string AvailableHoursDisplay => View.Goal?.AvailableHours is { } available ? $"{available:0.#} h" : "—";
}
