using System.Text;
using Contracts;
using Microsoft.AspNetCore.Http;
using WebMVC.Live;

namespace WebMVC.Tests;

/// <summary>
/// #286: <b>the stream's whole behaviour, driven by a fixed sequence of revisions.</b>
/// <para>
/// <b>Why an enumerable and not a source plus a clock.</b> The claim under test is "write an event when the
/// revision changes, and not when it does not". Observing that through a real timer would mean sleeping for
/// a tick - a slow test that is also flaky on a loaded machine. Handing the writer an exact sequence makes
/// the assertion about the wire format and nothing else, and the test finishes in microseconds.
/// </para>
/// <para>
/// The writer is production code, unmodified: <c>WriteAsync</c> takes its readings from an enumerable
/// precisely so this seam exists. The only thing replaced is where the numbers come from.
/// </para>
/// </summary>
public sealed class DashboardStreamTests
{
    private static DashboardRevision Revision(int paidOrders, decimal amount = 0m) => new(
        PaidOrdersCount: paidOrders,
        PaidOrdersAmount: amount,
        ComplaintsOpen: 0,
        ComplaintsReviewing: 0,
        ComplaintsResolved: 0,
        RefundsRequested: 0,
        RefundsApproved: 0,
        RefundsSettled: 0,
        RefundsRejected: 0,
        SettledMoneyRefundsCount: 0,
        SettledMoneyRefundsAmount: 0m);

    /// <summary>
    /// The control: three identical readings produce one baseline and two heartbeats - never a
    /// <c>revision</c> event. A stream that wrote an event per tick would pass a test that only counted bytes,
    /// and would make every dashboard refetch every thirty seconds for no reason.
    /// </summary>
    [Fact]
    public async Task An_unchanged_revision_writes_no_change_event()
    {
        var body = await Run(Revision(1), Revision(1), Revision(1));

        Assert.DoesNotContain("event: revision", body);

        // And the heartbeats are there, because the time-based alert needs them.
        Assert.Equal(2, Occurrences(body, "event: heartbeat"));
    }

    /// <summary>
    /// One change, one event - and exactly one. Two would refetch the fragment twice for one movement.
    /// </summary>
    [Fact]
    public async Task A_changed_revision_writes_exactly_one_change_event()
    {
        var body = await Run(Revision(1), Revision(2));

        Assert.Equal(1, Occurrences(body, "event: revision"));
        Assert.Equal(0, Occurrences(body, "event: heartbeat"));
    }

    /// <summary>
    /// The first reading is a baseline: the page has just rendered the same figures, so announcing them
    /// would buy one pointless fragment fetch per page load.
    /// </summary>
    [Fact]
    public async Task The_first_reading_is_a_baseline_and_is_not_announced()
    {
        var body = await Run(Revision(7), Revision(7));

        Assert.Equal(1, Occurrences(body, "event: heartbeat"));
        Assert.DoesNotContain("event: revision", body);
    }

    /// <summary>
    /// The event names are the contract the script listens for; a rename that quietly stopped the page
    /// updating would otherwise pass every test above. And the frame ends with a blank line, without which
    /// the browser buffers the event and delivers nothing.
    /// </summary>
    [Fact]
    public async Task The_wire_format_is_a_named_event_terminated_by_a_blank_line()
    {
        var body = await Run(Revision(1), Revision(2));

        Assert.Contains("event: revision\n", body);
        Assert.Contains("data: ", body);
        Assert.Contains("\n\n", body);

        // The headers say "this never ends and nothing may cache or buffer it".
        var response = await RunResponse(Revision(1), Revision(2));
        Assert.Equal("text/event-stream", response.ContentType);
        Assert.Equal("no-store", response.Headers.CacheControl.ToString());
        Assert.Equal("no", response.Headers["X-Accel-Buffering"].ToString());
    }

    /// <summary>
    /// The connection must be observable before the first tick.
    /// <para>
    /// SSE dispatches <c>open</c> only when bytes arrive, and the first tick is thirty seconds away, so a
    /// freshly loaded dashboard sat on "Connecting..." for half a minute while the stream was healthy. Found
    /// in a browser, not here: the stream answered 200 with <c>text/event-stream</c> and then wrote nothing.
    /// The leading comment line is what fixes it, and it must come before any reading.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_stream_writes_a_comment_first_so_the_connection_is_observable_at_once()
    {
        var body = await Run(Revision(7), Revision(7));

        Assert.StartsWith(": connected\n\n", body);

        // A comment dispatches no event, so it must not be mistaken for one.
        Assert.Equal(0, Occurrences(body, "event: connected"));
    }

    /// <summary>
    /// Slack's SSE convention, and the reason the comment is ':' rather than a named event: the browser
    /// dispatches nothing for it. Pinned as a literal so a future edit that "tidies" it into an event fails
    /// here rather than in a browser nobody is watching.
    /// </summary>
    [Fact]
    public async Task The_comment_line_is_not_delivered_as_an_event_name()
    {
        var body = await Run(Revision(1), Revision(2));

        var firstLine = body.Split('\n')[0];
        Assert.Equal(": connected", firstLine);
    }

    private static async Task<string> Run(params DashboardRevision[] readings)
    {
        var response = await RunResponse(readings);
        return Encoding.UTF8.GetString(((MemoryStream)response.Body).ToArray());
    }

    private static async Task<HttpResponse> RunResponse(params DashboardRevision[] readings)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await DashboardStream.WriteAsync(context.Response, Enumerate(readings), CancellationToken.None);

        return context.Response;
    }

    /// <summary>The enumerable the writer consumes in production too; only its values are stubbed here.</summary>
    private static async IAsyncEnumerable<DashboardRevision> Enumerate(DashboardRevision[] readings)
    {
        foreach (var reading in readings)
        {
            yield return reading;
            await Task.Yield();
        }
    }

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }
}
