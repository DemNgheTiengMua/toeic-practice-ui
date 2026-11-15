
namespace Contracts;

/// <summary>
/// #284: the dashboard's money readouts. Both revenue figures are <b>net</b> - paid orders minus money
/// refunds settled in the same window - and both are allowed to be negative, because a day or month whose
/// refunds exceed its income earned nothing and reporting it as zero would hide the one month an owner
/// needs to read. <c>CreditsRefundedInPeriod</c> is separate on purpose: a credit refund moved no money,
/// so folding it into revenue would state a figure that does not reconcile with a bank statement.
/// <para>
/// #351: every <c>…InPeriod</c> field follows <see cref="PeriodLabel"/>, which the caller chose. The two
/// <c>…Today</c> fields do not, and keep their names because a live snapshot is not a period. A field
/// still named "this month" while returning September would be read as the current month by the next
/// person to open this record.
/// </para>
/// </summary>
public sealed record AdminDashboardDto(
    int ActiveLearnersCount,
    int NewLearners7DaysCount,
    int CreditsUsedInPeriod,
    int CreditsRefundedInPeriod,
    decimal CreditsPerDayAverage,
    decimal RevenueTodayVnd,
    decimal RevenueInPeriodVnd,
    int PaidOrdersInPeriodCount,
    IReadOnlyList<MostAttemptedExamDto> MostAttemptedExams,
    AdminNeedsAttentionDto NeedsAttention,
    string PeriodLabel)
{
    public static AdminDashboardDto Empty => new(
        ActiveLearnersCount: 0,
        NewLearners7DaysCount: 0,
        CreditsUsedInPeriod: 0,
        CreditsRefundedInPeriod: 0,
        CreditsPerDayAverage: 0m,
        RevenueTodayVnd: 0m,
        RevenueInPeriodVnd: 0m,
        PaidOrdersInPeriodCount: 0,
        MostAttemptedExams: [],
        NeedsAttention: new AdminNeedsAttentionDto(0, 0, 0),
        PeriodLabel: string.Empty);
}
