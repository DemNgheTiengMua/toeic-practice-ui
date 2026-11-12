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
        var monthWindow = ReportingPeriod.Month(now);
        var dayWindow = ReportingPeriod.Day(now);
        var startOfMonth = monthWindow.Start;
        var endOfMonth = monthWindow.End;
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
        // Both halves are bounded on BOTH ends. A start-only filter is correct until the clock crosses into
        // next month, at which point next month's rows silently join this month's figure - and on a dashboard
        // nobody notices a total that is merely too large.
        var debitedDelta = await db.CreditTransactions
            .AsNoTracking()
            .Where(t => t.CreatedAt >= startOfMonth && t.CreatedAt < endOfMonth && t.Delta < 0 && t.Reason != "admin_revoke")
            .SumAsync(t => (int?)t.Delta, cancellationToken) ?? 0;
        var creditsDebited = -debitedDelta;

        // #284: a credit refund and a booking cancellation both give credits back, so both reduce what the
        // learner spent. Counting only the cancellation - which is what this did - understates the figure by
        // every refund ever issued. The codes are hoisted for the same reason PaidIn hoists its status: a
        // method call inside the expression is not something SQL Server can be asked to evaluate.
        var bookingCancel = CreditReason.BookingCancel.ToCode();
        var refundCredit = CreditReason.RefundCredit.ToCode();
        var creditsRefunded = await db.CreditTransactions
            .AsNoTracking()
            .Where(t => t.CreatedAt >= startOfMonth && t.CreatedAt < endOfMonth
                && (t.Reason == bookingCancel || t.Reason == refundCredit))
            .SumAsync(t => (int?)t.Delta, cancellationToken) ?? 0;

        var creditsUsedThisMonth = Math.Max(0, creditsDebited - creditsRefunded);

        var dayOfMonth = Math.Max(1, ReportingPeriod.LocalInstant(now).Day);
        var creditsPerDayAverage = Math.Round((decimal)creditsUsedThisMonth / dayOfMonth, 1);

        // 3. Net Revenue (Today and This Month)
        // #284 / ADR-0014: revenue is paid orders minus money refunds settled in the same window, and a
        // negative answer is a fact rather than a defect - a day whose refunds exceed its income is a loss.
        // Both halves ask ReportingPeriod, so the two money figures cannot drift apart from each other or
        // from the orders screen.
        var paidOrdersQuery = db.Orders
            .AsNoTracking()
            .PaidIn(monthWindow);

        var paidOrdersThisMonthCount = await paidOrdersQuery.CountAsync(cancellationToken);
        var paidThisMonthVnd = await paidOrdersQuery.SumAsync(o => (decimal?)o.Amount, cancellationToken) ?? 0m;

        var refundedThisMonthVnd = await db.Refunds
            .AsNoTracking()
            .MoneyRefundedIn(monthWindow)
            .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m;

        var paidTodayVnd = await db.Orders
            .AsNoTracking()
            .PaidIn(dayWindow)
            .SumAsync(o => (decimal?)o.Amount, cancellationToken) ?? 0m;

        var refundedTodayVnd = await db.Refunds
            .AsNoTracking()
            .MoneyRefundedIn(dayWindow)
            .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m;

        var revenueThisMonthVnd = paidThisMonthVnd - refundedThisMonthVnd;
        var revenueTodayVnd = paidTodayVnd - refundedTodayVnd;

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
            CreditsRefundedThisMonth: creditsRefunded,
            CreditsPerDayAverage: creditsPerDayAverage,
            RevenueTodayVnd: revenueTodayVnd,
            RevenueThisMonthVnd: revenueThisMonthVnd,
            PaidOrdersThisMonthCount: paidOrdersThisMonthCount,
            MostAttemptedExams: mostAttemptedExams,
            NeedsAttention: needsAttention);
    }
}
