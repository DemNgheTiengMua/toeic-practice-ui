using System.Runtime.CompilerServices;
using System.Text;
using Contracts;

namespace WebMVC.Live;

/// <summary>
/// #286 / ADR-0013: <b>the server-sent event stream, and the two different reasons it writes.</b>
/// <para>
/// <b>Why the stream carries no payload.</b> The event data is a comparison token, never a figure. If the
/// stream carried amounts, the browser would have to format money - and then the server's formatter and the
/// script's formatter would be two implementations of one rule, drifting apart the first time either
/// changed. The browser gets told <i>that</i> something moved, and fetches the rendered fragment; every
/// number is formatted exactly once, on the server.
/// </para>
/// <para>
/// <b>Why there is a heartbeat.</b> One dashboard alert is time-based, not row-based: orders waiting over
/// thirty minutes for reconciliation. It becomes due with no row changing, so a stream that only wrote on a
/// revision change would leave that alert unable to appear on its own. Each tick therefore writes one of two
/// things - a <c>revision</c> event when the value moved, a <c>heartbeat</c> when it did not - and the page
/// refetches on either. The two names are kept apart so "nothing changed" stays observable: a heartbeat is
/// evidence the connection is alive, not evidence the figures moved.
/// </para>
/// <para>
/// <b>The first reading is a baseline, not an event.</b> The page has just rendered the same figures from
/// the same source, so writing them again would buy one redundant fragment fetch per page load. A change
/// between the render and the connect is caught on the next tick.
/// </para>
/// </summary>
public static class DashboardStream
{
    /// <summary>The revision changed. The page refetches the metric region.</summary>
    public const string ChangeEvent = "revision";

    /// <summary>Nothing changed, but time passed - which is the only way a time-based alert can appear.</summary>
    public const string HeartbeatEvent = "heartbeat";

    /// <summary>
    /// One tick every thirty seconds: the coarse half of the "waiting over 30 minutes" alert. The alert
    /// crosses its own threshold on a thirty-minute boundary, so a thirty-second tick makes it visible
    /// within a fraction of the granularity it is measured at, and costs one grouped query per connected
    /// admin per tick - not per second.
    /// </summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Writes the stream until <paramref name="readings"/> completes or the client goes away. Takes the
    /// readings as an enumerable rather than reading a source itself, so a test can drive an exact sequence
    /// of revisions and assert what appeared on the wire - without a database, and without sleeping.
    /// </summary>
    public static async Task WriteAsync(
        HttpResponse response,
        IAsyncEnumerable<DashboardRevision> readings,
        CancellationToken cancellationToken)
    {
        response.ContentType = "text/event-stream";
        // no-store: a cached stream is a stream that never changes. X-Accel-Buffering: a reverse proxy that
        // buffers turns this into one large response delivered when the connection closes, which is never.
        response.Headers.CacheControl = "no-store";
        response.Headers["X-Accel-Buffering"] = "no";

        // A comment line, then the first reading is taken as a baseline. Both matters:
        //
        // The comment is what makes the connection observable. SSE dispatches `open` to the client only once
        // bytes arrive, and the first tick is thirty seconds away, so without this a freshly loaded
        // dashboard sits on "Connecting..." for half a minute while the stream is in fact healthy. Measured:
        // twenty seconds of a browser showing "Connecting..." on a stream that had already answered 200 with
        // text/event-stream. A line beginning with ':' is a comment per the SSE format and dispatches no
        // event, so this announces liveness without buying the redundant fragment fetch a real event would.
        await WriteRawAsync(response, ": connected\n\n", cancellationToken);

        DashboardRevision? baseline = null;

        try
        {
            await foreach (var reading in readings.WithCancellation(cancellationToken))
            {
                if (baseline is null)
                {
                    baseline = reading;
                    continue;
                }

                if (reading != baseline)
                {
                    baseline = reading;
                    await WriteEventAsync(response, ChangeEvent, reading.Token, cancellationToken);
                }
                else
                {
                    await WriteEventAsync(response, HeartbeatEvent, reading.Token, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The admin closed the tab or navigated away. That is the expected end of a long-lived request,
            // not a fault: nothing is logged and nothing is rethrown, or every closed dashboard would leave
            // an exception in the log and read as an outage.
        }
    }

    private static async Task WriteRawAsync(HttpResponse response, string text, CancellationToken cancellationToken)
    {
        await response.WriteAsync(text, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static async Task WriteEventAsync(
        HttpResponse response, string name, string data, CancellationToken cancellationToken)
    {
        // One write per event: two writes can be interleaved by another response on a shared connection and
        // produce a frame the browser discards as malformed.
        var frame = new StringBuilder()
            .Append("event: ").Append(name).Append('\n')
            .Append("data: ").Append(data).Append('\n')
            .Append('\n')
            .ToString();

        await response.WriteAsync(frame, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// The production readings: read now, then once per tick. Exists so the controller holds no loop and the
    /// tests never construct one.
    /// </summary>
    public static async IAsyncEnumerable<DashboardRevision> ReadingsAsync(
        IDashboardRevisionSource source,
        TimeSpan interval,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var timer = new PeriodicTimer(interval);
        yield return await source.ReadAsync(cancellationToken);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            yield return await source.ReadAsync(cancellationToken);
        }
    }
}
