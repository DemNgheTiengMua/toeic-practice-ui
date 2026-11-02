using Contracts;

namespace WebMVC.Models;

public sealed record ExamHistoryViewModel(ExamHistoryDto History, string? ErrorMessage = null)
{
    public IReadOnlyList<ExamHistoryAttemptViewModel> Attempts { get; init; } = [];
    public IReadOnlyList<ExamHistoryChartPointViewModel> ChartPoints { get; init; } = [];

    /// <summary>#161: the strip's columns are the certificate's bands, each as wide as the score range it
    /// covers. Ordering by MinTotal rather than trusting the delivered order is what makes the aria-label
    /// and the left-hand tick name the band that really starts the scale.</summary>
    public BandStripViewModel BandStrip
    {
        get
        {
            var ordered = History.CefrBands.OrderBy(b => b.MinTotal).ToList();
            return new BandStripViewModel(
                ordered.Select(b => new BandStripItemViewModel(b.Code, b.MinTotal, b.MaxTotal,
                    "cefr__band" + (b.Code == History.BestScoreBandCode ? " is-current"
                        : History.BestScore >= b.MinTotal ? " is-reached" : "")))
                    .ToList(),
                ordered.Count == 0
                    ? "No score bands configured"
                    : $"Current band {History.BestScoreBandCode}: {ordered[0].Code} at {ordered[0].MinTotal} to {ordered[^1].Code} at {ordered[^1].MaxTotal}");
        }
    }
}
