using BusinessObjects;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #286 / ADR-0013: the revision, read as counts and sums over already-indexed columns.
/// <para>
/// <b>Why these eleven figures and no more.</b> Each one is moved by a change the dashboard must show: a
/// paid order moves the paid count and sum, a complaint status change moves one of the three buckets to
/// another, and a refund decision moves its own bucket - with a money settlement moving the last pair, which
/// is what net revenue subtracts. Nothing here reads a timestamp, so there is no column to add and no
/// trigger to keep correct.
/// </para>
/// <para>
/// <b>The revision is not windowed.</b> A reporting window would need the clock, and a revision that moved
/// because the clock crossed a month boundary would report a change on a dashboard whose figures had not
/// moved. The counts are all-time, so they change only when a row changes - which is the definition ADR-0013
/// asks for.
/// </para>
/// <para>
/// <b>No index was added, and that is the deliberate answer to the ticket's index criterion.</b> The two
/// status groupings are already served by <c>IX_Complaints_Status (Status, CreatedAt)</c> and
/// <c>IX_Refunds_Status (Status, CreatedAt)</c>. The remaining two walk <c>Orders</c>, which holds one row per
/// purchase ever made - thousands of narrow rows, not a hot log. A filtered index on <c>Status IN
/// ('paid')</c> cannot be measured to earn its write cost at that size because no plan would choose it yet,
/// so adding one now would be an unmeasurable guess rather than a measurement. Re-measure when
/// <c>Orders</c> reaches six figures; the change then is a filtered index on <c>Orders(Status) INCLUDE
/// (Amount)</c>.
/// </para>
/// </summary>
public sealed class DashboardRevisionService(CompanionDbContext db) : IDashboardRevisionService
{
    public async Task<DashboardRevision> GetRevisionAsync(CancellationToken cancellationToken = default)
    {
        var paid = OrderStatus.Paid.ToCode();

        // Count and sum in one round trip: two statements could read a table the first has already moved
        // past, and the pair would then describe two different states of the same table.
        var paidOrders = await db.Orders
            .AsNoTracking()
            .Where(o => o.Status == paid)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Amount = g.Sum(o => o.Amount) })
            .FirstOrDefaultAsync(cancellationToken);

        var complaintsByStatus = await db.Complaints
            .AsNoTracking()
            .GroupBy(c => c.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

        var refundsByStatus = await db.Refunds
            .AsNoTracking()
            .GroupBy(r => r.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

        var money = RefundMethod.Money.ToCode();
        var settledMoney = await db.Refunds
            .AsNoTracking()
            .Where(r => r.Method == money && r.SettledAt != null)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Amount = g.Sum(r => r.Amount ?? 0m) })
            .FirstOrDefaultAsync(cancellationToken);

        return new DashboardRevision(
            PaidOrdersCount: paidOrders?.Count ?? 0,
            PaidOrdersAmount: paidOrders?.Amount ?? 0m,
            ComplaintsOpen: Bucket(complaintsByStatus, ComplaintStatus.Open),
            ComplaintsReviewing: Bucket(complaintsByStatus, ComplaintStatus.Reviewing),
            ComplaintsResolved: Bucket(complaintsByStatus, ComplaintStatus.Resolved),
            RefundsRequested: Bucket(refundsByStatus, RefundStatus.Requested),
            RefundsApproved: Bucket(refundsByStatus, RefundStatus.Approved),
            RefundsSettled: Bucket(refundsByStatus, RefundStatus.Settled),
            RefundsRejected: Bucket(refundsByStatus, RefundStatus.Rejected),
            SettledMoneyRefundsCount: settledMoney?.Count ?? 0,
            SettledMoneyRefundsAmount: settledMoney?.Amount ?? 0m);
    }

    /// <summary>A status with no rows is absent from a grouping rather than present as a zero.</summary>
    private static int Bucket<TEnum>(IReadOnlyDictionary<string, int> counts, TEnum status)
        where TEnum : struct, Enum =>
        counts.TryGetValue(status.ToCode(), out var count) ? count : 0;
}
