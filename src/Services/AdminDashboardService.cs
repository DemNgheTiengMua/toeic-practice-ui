using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed class AdminDashboardService(CompanionDbContext db, TimeProvider clock) : IAdminDashboardService
{
    public async Task<AdminDashboardDto> GetDashboardMetricsAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var startOfMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var sevenDaysAgo = now.AddDays(-7);
        var thirtyMinutesAgo = now.AddMinutes(-30);

        // 1. Active Learners
        var activeLearnersCount = await db.Users
            .AsNoTracking()
            .Where(u => !u.IsDeleted && u.Role.Code == Roles.Learner && !u.IsLocked && u.EmailVerifiedAt != null)
            .CountAsync(cancellationToken);

        var newLearners7DaysCount = await db.Users
            .AsNoTracking()
            .Where(u => !u.IsDeleted && u.Role.Code == Roles.Learner && u.CreatedAt >= sevenDaysAgo)
            .CountAsync(cancellationToken);

        // 2. Credits Used (This Month)
        var debitedDelta = await db.CreditTransactions
            .AsNoTracking()
            .Where(t => t.CreatedAt >= startOfMonth && t.Delta < 0 && t.Reason != "admin_revoke")
            .SumAsync(t => (int?)t.Delta, cancellationToken) ?? 0;
        var creditsDebited = -debitedDelta;

        var creditsRefunded = await db.CreditTransactions
            .AsNoTracking()
            .Where(t => t.CreatedAt >= startOfMonth && t.Reason == "booking_cancel")
            .SumAsync(t => (int?)t.Delta, cancellationToken) ?? 0;

        var creditsUsedThisMonth = Math.Max(0, creditsDebited - creditsRefunded);

        var dayOfMonth = Math.Max(1, now.Day);
        var creditsPerDayAverage = Math.Round((decimal)creditsUsedThisMonth / dayOfMonth, 1);

        // 3. Revenue (This Month)
        var paidOrdersQuery = db.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.Paid.ToCode() &&
                        (o.PaidAt >= startOfMonth || (o.PaidAt == null && o.CreatedAt >= startOfMonth)));

        var paidOrdersThisMonthCount = await paidOrdersQuery.CountAsync(cancellationToken);
        var revenueThisMonthVnd = await paidOrdersQuery.SumAsync(o => (decimal?)o.Amount, cancellationToken) ?? 0m;

        // 4. Most Attempted Exams (Top 5)
        var topExams = await db.ExamAttempts
            .AsNoTracking()
            .Where(a => (a.Status == "done" || a.Status == AttemptStatus.Graded.ToCode() || a.Status == AttemptStatus.Submitted.ToCode()) && !a.Exam.IsDeleted)
            .GroupBy(a => new { a.ExamId, a.Exam.Name })
            .Select(g => new
            {
                g.Key.ExamId,
                g.Key.Name,
                AttemptsCount = g.Count(),
                AverageScore = g.Average(a => (decimal?)a.TotalScore)
            })
            .OrderByDescending(x => x.AttemptsCount)
            .ThenBy(x => x.Name)
            .Take(5)
            .ToListAsync(cancellationToken);

        var mostAttemptedExams = topExams
            .Select(x => new MostAttemptedExamDto(
                x.ExamId,
                x.Name,
                x.AttemptsCount,
                x.AverageScore.HasValue ? Math.Round(x.AverageScore.Value, 1) : null))
            .ToList();

        // 5. Needs Attention / System Alerts
        var draftExamsCount = await db.Exams
            .AsNoTracking()
            .CountAsync(e => !e.IsDeleted && e.Status == ExamStatus.Draft.ToCode(), cancellationToken);

        var stalePendingOrdersCount = await db.Orders
            .AsNoTracking()
            .CountAsync(o => o.Status == OrderStatus.Pending.ToCode() && o.CreatedAt < thirtyMinutesAgo, cancellationToken);

        var openComplaintsCount = await db.Complaints
            .AsNoTracking()
            .CountAsync(c => c.Status == ComplaintStatus.Open.ToCode() || c.Status == ComplaintStatus.Reviewing.ToCode(), cancellationToken);

        var needsAttention = new AdminNeedsAttentionDto(
            draftExamsCount,
            stalePendingOrdersCount,
            openComplaintsCount);

        return new AdminDashboardDto(
            ActiveLearnersCount: activeLearnersCount,
            NewLearners7DaysCount: newLearners7DaysCount,
            CreditsUsedThisMonth: creditsUsedThisMonth,
            CreditsPerDayAverage: creditsPerDayAverage,
            RevenueThisMonthVnd: revenueThisMonthVnd,
            PaidOrdersThisMonthCount: paidOrdersThisMonthCount,
            MostAttemptedExams: mostAttemptedExams,
            NeedsAttention: needsAttention);
    }
}
