namespace WebMVC.Live;

/// <summary>
/// #286: how often the dashboard stream reads the revision. A value rather than a constant so the endpoint
/// test can drive the real route without waiting thirty seconds for its first tick - and so the cadence is
/// tunable in a deployment where a thirty-second query per connected admin is the wrong trade.
/// </summary>
public sealed record DashboardLiveOptions(TimeSpan TickInterval)
{
    /// <summary>Thirty seconds: the coarse half of a thirty-minute alert, per <see cref="DashboardStream"/>.</summary>
    public static DashboardLiveOptions Default => new(DashboardStream.TickInterval);
}
