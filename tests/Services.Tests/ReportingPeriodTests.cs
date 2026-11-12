using BusinessObjects;
using BusinessObjects.Entities;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #279: the reporting calendar itself. These are the boundaries the two admin screens now share, so the
/// accountant's day and month are asserted here once rather than re-derived in each screen's test.
/// </summary>
public sealed class ReportingPeriodTests
{
    private static readonly TimeSpan Vn = ReportingPeriod.UtcOffset;

    private static Order Paid(DateTimeOffset? paidAt, DateTimeOffset createdAt) => new()
    {
        Status = OrderStatus.Paid.ToCode(),
        PaidAt = paidAt,
        CreatedAt = createdAt,
        Amount = 100000m,
    };

    private static IQueryable<Order> Orders(params Order[] orders) => orders.AsQueryable();

    /// <summary>
    /// 23:30 on 31 October in Vietnam is 16:30Z the same day. Shifting the boundary by UTC instead of by
    /// the accountant's offset files this order in November, and October closes one order early.
    /// </summary>
    [Fact]
    public void An_order_paid_at_half_past_eleven_at_night_belongs_to_that_day_and_that_month()
    {
        var paidAt = new DateTimeOffset(2026, 10, 31, 23, 30, 0, Vn);
        var order = Paid(paidAt, paidAt.AddMinutes(-5));

        Assert.Equal(new DateTimeOffset(2026, 10, 31, 0, 0, 0, Vn), ReportingPeriod.Day(paidAt).Start);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, Vn), ReportingPeriod.Day(paidAt).End);
        Assert.Single(Orders(order).PaidIn(ReportingPeriod.Day(paidAt)));
        Assert.Single(Orders(order).PaidIn(ReportingPeriod.Month(paidAt)));
    }

    /// <summary>
    /// 00:30 on 1 November in Vietnam is 17:30Z on 31 October. A month cut at UTC midnight puts this order
    /// in the month that just closed, which is the seven-hour error the ticket exists for.
    /// </summary>
    [Fact]
    public void An_order_paid_just_after_local_midnight_belongs_to_the_new_month()
    {
        var paidAt = new DateTimeOffset(2026, 11, 1, 0, 30, 0, Vn);
        var october = ReportingPeriod.Month(new DateTimeOffset(2026, 10, 31, 12, 0, 0, Vn));
        var november = ReportingPeriod.Month(paidAt);

        Assert.Empty(Orders(Paid(paidAt, paidAt)).PaidIn(october));
        Assert.Single(Orders(Paid(paidAt, paidAt)).PaidIn(november));
    }

    /// <summary>
    /// A paid order with no payment stamp is counted once, on the only date it carries - not once for the
    /// month its creation falls in and again for whatever a missing stamp might mean.
    /// </summary>
    [Fact]
    public void A_paid_order_with_no_payment_stamp_is_counted_once_by_its_creation_date()
    {
        var createdAt = new DateTimeOffset(2026, 10, 5, 10, 0, 0, Vn);
        var orders = Orders(Paid(null, createdAt));

        Assert.Single(orders.PaidIn(ReportingPeriod.Month(createdAt)));
        Assert.Empty(orders.PaidIn(ReportingPeriod.Month(createdAt.AddMonths(-1))));
        Assert.Empty(orders.PaidIn(ReportingPeriod.Month(createdAt.AddMonths(1))));
    }

    /// <summary>An unpaid order never counts, whatever its timestamps say.</summary>
    [Fact]
    public void A_pending_order_is_never_paid_in_the_period()
    {
        var at = new DateTimeOffset(2026, 10, 5, 10, 0, 0, Vn);
        var order = Paid(at, at);
        order.Status = OrderStatus.Pending.ToCode();

        Assert.Empty(Orders(order).PaidIn(ReportingPeriod.Month(at)));
    }

    /// <summary>Consecutive months meet at one instant, and exactly one of them claims it.</summary>
    [Fact]
    public void The_boundary_instant_belongs_to_exactly_one_month()
    {
        var september = ReportingPeriod.Month(new DateTimeOffset(2026, 9, 15, 12, 0, 0, Vn));
        var october = ReportingPeriod.Month(september.End);
        var atBoundary = Paid(september.End, september.End);

        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, Vn), september.End);
        Assert.Empty(Orders(atBoundary).PaidIn(september));
        Assert.Single(Orders(atBoundary).PaidIn(october));
    }
}
