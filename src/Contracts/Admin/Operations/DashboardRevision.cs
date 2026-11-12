namespace Contracts;

/// <summary>
/// #286 / ADR-0013: <b>the dashboard's change detector is a tuple of counts and sums, not a timestamp.</b>
/// <para>
/// <b>Why not an <c>UpdatedAt</c> or a <c>rowversion</c>.</b> No such column exists, and adding one would
/// mean a trigger on the two hottest tables in the domain, a <c>HasTrigger</c> declaration to stop the
/// <c>OUTPUT</c> clause failing, and a column every future writer must keep correct forever. Every change
/// the dashboard must reflect already moves a countable aggregate, so those aggregates <i>are</i> the
/// revision.
/// </para>
/// <para>
/// <b>The known blind spot, and it is deliberate.</b> An edit that changes neither a count nor a sum is
/// invisible here. That is correct for this surface - it shows only counts and sums - and it is why this
/// type must not be reused for a surface showing free text without revisiting ADR-0013.
/// </para>
/// <para>
/// <b>Equality is the whole contract.</b> The struct is a value record, so <c>==</c> is the change test and
/// nothing else is needed. That is what makes the comparison testable without HTTP and without a database
/// in the web-app tests.
/// </para>
/// <para>
/// Every figure is all-time and unwindowed on purpose: a window would need a clock, and a clock would make
/// the revision disagree with itself across a month boundary. The heartbeat covers the one thing a
/// row-based revision cannot see - a time-based alert becoming due with no row changing.
/// </para>
/// </summary>
public readonly record struct DashboardRevision(
    int PaidOrdersCount,
    decimal PaidOrdersAmount,
    int ComplaintsOpen,
    int ComplaintsReviewing,
    int ComplaintsResolved,
    int RefundsRequested,
    int RefundsApproved,
    int RefundsSettled,
    int RefundsRejected,
    int SettledMoneyRefundsCount,
    decimal SettledMoneyRefundsAmount)
{
    public static DashboardRevision Empty => default;

    /// <summary>
    /// The stream's event data. The browser only compares event delivery to decide when to refetch, and
    /// the fragment renders every figure through <c>CurrencyFormatter</c> on the server - so no client
    /// script ever formats a number and no second formatter can drift from the first. The token exists to
    /// make the stream readable in a developer tool, not to be parsed.
    /// </summary>
    public string Token => string.Join('|',
        PaidOrdersCount, PaidOrdersAmount,
        ComplaintsOpen, ComplaintsReviewing, ComplaintsResolved,
        RefundsRequested, RefundsApproved, RefundsSettled, RefundsRejected,
        SettledMoneyRefundsCount, SettledMoneyRefundsAmount);
}
