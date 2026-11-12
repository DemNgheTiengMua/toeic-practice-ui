using Contracts;
namespace WebMVC.Models;

public sealed record AdminDashboardViewModel(AdminDashboardDto Dashboard)
{
    /// <summary>#284: both revenue figures are net and both may be negative.</summary>
    private const string StatValueClass = "stat__value";
    private const string DangerStatValueClass = "stat__value text-danger";

    public string ActiveLearners { get; } = Dashboard.ActiveLearnersCount.ToString("N0");
    public string NewLearners { get; } = Dashboard.NewLearners7DaysCount.ToString("N0");
    public string CreditsUsed { get; } = Dashboard.CreditsUsedThisMonth.ToString("N0");
    public string CreditsRefunded { get; } = Dashboard.CreditsRefundedThisMonth.ToString("N0");
    public string CreditsPerDay { get; } = Math.Round(Dashboard.CreditsPerDayAverage, 0).ToString("N0");
    public string RevenueToday { get; } = CurrencyFormatter.FormatRevenue(Dashboard.RevenueTodayVnd);
    public string RevenueThisMonth { get; } = CurrencyFormatter.FormatRevenue(Dashboard.RevenueThisMonthVnd);

    /// <summary>
    /// #284 / §5: a negative figure is a refused condition, so it is lettered in Sign Red. The formatter
    /// already emits U+2212; this decides only the colour, and a zero or positive figure keeps the plain
    /// class so no existing screen changes.
    /// </summary>
    public string RevenueTodayClass { get; } =
        Dashboard.RevenueTodayVnd < 0 ? DangerStatValueClass : StatValueClass;

    public string RevenueThisMonthClass { get; } =
        Dashboard.RevenueThisMonthVnd < 0 ? DangerStatValueClass : StatValueClass;

    public string PaidOrders { get; } = Dashboard.PaidOrdersThisMonthCount.ToString("N0");
    public bool HasAlerts { get; } = Dashboard.NeedsAttention.DraftExamsCount > 0
        || Dashboard.NeedsAttention.StalePendingOrdersCount > 0 || Dashboard.NeedsAttention.OpenComplaintsCount > 0;
    public IReadOnlyDictionary<int, string> Attempts { get; } = Dashboard.MostAttemptedExams.ToDictionary(e => e.ExamId, e => e.AttemptsCount.ToString("N0"));
    public IReadOnlyDictionary<int, string> Scores { get; } = Dashboard.MostAttemptedExams.ToDictionary(e => e.ExamId,
        e => e.AverageScore.HasValue ? (e.AverageScore.Value % 1 == 0 ? e.AverageScore.Value.ToString("N0") : e.AverageScore.Value.ToString("0.#")) : "—");
}
