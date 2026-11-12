using BusinessObjects;
using BusinessObjects.Entities;

namespace Services;

/// <summary>
/// #279: <b>one definition of "a paid order in a reporting period", and one place that cuts the period.</b>
/// <para>
/// <b>Why one place.</b> The dashboard counted a paid order when <c>PaidAt</c> fell in the month, or when
/// <c>PaidAt</c> was missing and <c>CreatedAt</c> did; the orders screen counted only <c>PaidAt</c>. Two
/// admin screens, one question, two answers - an order paid but never stamped was invisible to the orders
/// screen and visible to the dashboard. <see cref="PaidIn"/> is now the only predicate either asks, so the
/// two totals cannot drift apart again.
/// </para>
/// <para>
/// <b>Why UTC+7 and not the machine.</b> The accountant reads a Vietnamese calendar. A month cut at UTC
/// midnight ends seven hours after they think it does, so an order paid at 23:30 on 31 October landed in
/// November. The offset is a constant here and is never read from configuration, the host, or a request,
/// so a container's timezone cannot move a report.
/// </para>
/// </summary>
public static class ReportingPeriod
{
    /// <summary>
    /// The reporting offset: Vietnam is UTC+7 all year and has no daylight saving, so a fixed offset is
    /// the whole rule. Never make this configurable - a report that changes with a deployment setting is
    /// a report two people can disagree about while both being "right".
    /// </summary>
    public static readonly TimeSpan UtcOffset = TimeSpan.FromHours(7);

    /// <summary>The instant on the accountant's calendar.</summary>
    public static DateTimeOffset LocalInstant(DateTimeOffset utcNow) => utcNow.ToOffset(UtcOffset);

    /// <summary>The calendar day containing <paramref name="utcNow"/>, as the accountant sees it.</summary>
    public static ReportingWindow Day(DateTimeOffset utcNow)
    {
        var local = LocalInstant(utcNow);
        var start = new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, UtcOffset);
        return new ReportingWindow(start, start.AddDays(1));
    }

    /// <summary>The calendar month containing <paramref name="utcNow"/>, as the accountant sees it.</summary>
    public static ReportingWindow Month(DateTimeOffset utcNow)
    {
        var local = LocalInstant(utcNow);
        var start = new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, UtcOffset);
        return new ReportingWindow(start, start.AddMonths(1));
    }

    /// <summary>
    /// A half-open window <c>[Start, End)</c>. Half-open is what makes the count exact: consecutive periods
    /// share a boundary instant but never both claim it, so no order is counted twice and none is dropped.
    /// </summary>
    public readonly record struct ReportingWindow(DateTimeOffset Start, DateTimeOffset End);

    /// <summary>
    /// The orders paid inside <paramref name="window"/>. Stays an <see cref="IQueryable{T}"/> so the filter
    /// runs in SQL, and is the single definition both admin screens call.
    /// <para>
    /// The reporting date is <c>PaidAt</c>. It falls back to <c>CreatedAt</c> only when <c>PaidAt</c> is
    /// missing - a paid order with no stamp is still money that came in, and its creation is the only date
    /// the row carries. Coalescing before the comparison is what makes such a row land in exactly one
    /// period: it has one reporting instant, not one per date column.
    /// </para>
    /// </summary>
    public static IQueryable<Order> PaidIn(this IQueryable<Order> orders, ReportingWindow window)
    {
        var start = window.Start;
        var end = window.End;
        var paid = OrderStatus.Paid.ToCode();

        return orders.Where(o =>
            o.Status == paid &&
            (o.PaidAt ?? o.CreatedAt) >= start &&
            (o.PaidAt ?? o.CreatedAt) < end);
    }

    /// <summary>
    /// #284: the money that actually left inside <paramref name="window"/>, so net revenue subtracts it.
    /// <para>
    /// <b>Why settlement and not the request.</b> A refund is a promise until the accountant transfers the
    /// money; <c>SettledAt</c> is the only timestamp at which money is treated as having left. Attributing
    /// a refund to its request date would report a loss against revenue the business still held - and it
    /// would let a refund requested in one month land on a figure already closed.
    /// </para>
    /// <para>
    /// <b>Why only <c>money</c>.</b> A credit refund grants credits from the ledger; no money moved, so it
    /// cannot reduce revenue. It is counted in its own figure (credits refunded) instead.
    /// </para>
    /// </summary>
    public static IQueryable<Refund> MoneyRefundedIn(this IQueryable<Refund> refunds, ReportingWindow window)
    {
        var start = window.Start;
        var end = window.End;
        var money = RefundMethod.Money.ToCode();

        return refunds.Where(r =>
            r.Method == money &&
            r.SettledAt != null &&
            r.SettledAt >= start &&
            r.SettledAt < end);
    }
}
