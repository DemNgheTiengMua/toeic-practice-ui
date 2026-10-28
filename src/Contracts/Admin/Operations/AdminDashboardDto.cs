
namespace Contracts;

public sealed record AdminDashboardDto(
    int ActiveLearnersCount,
    int NewLearners7DaysCount,
    int CreditsUsedThisMonth,
    decimal CreditsPerDayAverage,
    decimal RevenueThisMonthVnd,
    int PaidOrdersThisMonthCount,
    IReadOnlyList<MostAttemptedExamDto> MostAttemptedExams,
    AdminNeedsAttentionDto NeedsAttention)
{
    public static AdminDashboardDto Empty => new(
        ActiveLearnersCount: 0,
        NewLearners7DaysCount: 0,
        CreditsUsedThisMonth: 0,
        CreditsPerDayAverage: 0m,
        RevenueThisMonthVnd: 0m,
        PaidOrdersThisMonthCount: 0,
        MostAttemptedExams: [],
        NeedsAttention: new AdminNeedsAttentionDto(0, 0, 0));
}
