
namespace Contracts;

/// <summary>
/// #284: the dashboard's money readouts. Both revenue figures are <b>net</b> - paid orders minus money
/// refunds settled in the same window - and both are allowed to be negative, because a day or month whose
/// refunds exceed its income earned nothing and reporting it as zero would hide the one month an owner
/// needs to read. <c>CreditsRefundedThisMonth</c> is separate on purpose: a credit refund moved no money,
/// so folding it into revenue would state a figure that does not reconcile with a bank statement.
/// </summary>
public sealed record AdminDashboardDto(
    int ActiveLearnersCount,
    int NewLearners7DaysCount,
    int CreditsUsedThisMonth,
    int CreditsRefundedThisMonth,
    decimal CreditsPerDayAverage,
    decimal RevenueTodayVnd,
    decimal RevenueThisMonthVnd,
    int PaidOrdersThisMonthCount,
    IReadOnlyList<MostAttemptedExamDto> MostAttemptedExams,
    AdminNeedsAttentionDto NeedsAttention)
{
    public static AdminDashboardDto Empty => new(
        ActiveLearnersCount: 0,
        NewLearners7DaysCount: 0,
        CreditsUsedThisMonth: 0,
        CreditsRefundedThisMonth: 0,
        CreditsPerDayAverage: 0m,
        RevenueTodayVnd: 0m,
        RevenueThisMonthVnd: 0m,
        PaidOrdersThisMonthCount: 0,
        MostAttemptedExams: [],
        NeedsAttention: new AdminNeedsAttentionDto(0, 0, 0));
}
