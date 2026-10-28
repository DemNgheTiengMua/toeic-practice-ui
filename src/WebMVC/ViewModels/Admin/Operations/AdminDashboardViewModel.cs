using Contracts;
namespace WebMVC.Models;

public sealed record AdminDashboardViewModel(AdminDashboardDto Dashboard)
{
    public string ActiveLearners { get; } = Dashboard.ActiveLearnersCount.ToString("N0");
    public string NewLearners { get; } = Dashboard.NewLearners7DaysCount.ToString("N0");
    public string CreditsUsed { get; } = Dashboard.CreditsUsedThisMonth.ToString("N0");
    public string CreditsPerDay { get; } = Math.Round(Dashboard.CreditsPerDayAverage, 0).ToString("N0");
    public string Revenue { get; } = CurrencyFormatter.FormatRevenue(Dashboard.RevenueThisMonthVnd);
    public string PaidOrders { get; } = Dashboard.PaidOrdersThisMonthCount.ToString("N0");
    public bool HasAlerts { get; } = Dashboard.NeedsAttention.DraftExamsCount > 0
        || Dashboard.NeedsAttention.StalePendingOrdersCount > 0 || Dashboard.NeedsAttention.OpenComplaintsCount > 0;
    public IReadOnlyDictionary<int, string> Attempts { get; } = Dashboard.MostAttemptedExams.ToDictionary(e => e.ExamId, e => e.AttemptsCount.ToString("N0"));
    public IReadOnlyDictionary<int, string> Scores { get; } = Dashboard.MostAttemptedExams.ToDictionary(e => e.ExamId,
        e => e.AverageScore.HasValue ? (e.AverageScore.Value % 1 == 0 ? e.AverageScore.Value.ToString("N0") : e.AverageScore.Value.ToString("0.#")) : "—");
}
