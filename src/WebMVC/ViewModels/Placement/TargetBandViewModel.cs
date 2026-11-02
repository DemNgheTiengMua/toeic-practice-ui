using Contracts;

namespace WebMVC.Models;

public sealed record TargetBandViewModel(
    TargetBandViewDto View,
    string? ErrorMessage = null,
    string? SuccessMessage = null)
{
    public int CurrentScore { get; } = View.CurrentTotalScore ?? 0;

    /// <summary>#161: the band the learner chose, or null when they have never chosen one. It used to
    /// fall back to the literal "B2" and to the first band that is not their current one, which put a
    /// band in the form that the learner never picked — and on a certificate with no B2, a band that is
    /// not even in the list. Null is the honest answer, and the page offers the choice.</summary>
    public string? TargetCode { get; } = View.TargetBandCode;

    public bool HasTargetBand => TargetCode is not null;

    public int PointsNeeded { get; init; }
    public string NeededDisplay { get; init; } = string.Empty;
    public IReadOnlyList<AttemptBandViewModel> Bands { get; init; } = [];

    /// <summary>#161: the strip is drawn from the certificate's bands, in score order.</summary>
    public BandStripViewModel BandStrip => new(
        View.CefrBands.OrderBy(b => b.MinTotal)
            .Select(b => new BandStripItemViewModel(b.Code, b.MinTotal, b.MaxTotal,
                "cefr__band" + (b.IsCurrent ? " is-current" : b.IsReached ? " is-reached" : "")))
            .ToList(),
        View.CurrentBandCode is null
            ? "No level assessed yet"
            : $"Current band {View.CurrentBandCode}, score {View.CurrentTotalScore}");

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
