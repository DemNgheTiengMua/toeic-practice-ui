using System.Net;
using Microsoft.AspNetCore.Http;

namespace WebMVC.Infrastructure;

/// <summary>
/// Bounds the wait on an unreachable API so a degraded page renders in seconds instead of the
/// HTTP default (#140). Three layers, cheapest first: a request that already lost an API call
/// fails its remaining calls at once, the process-wide breaker refuses calls while the outage
/// lasts, and the connect/header phases are capped.
/// <para>
/// Every rejection surfaces as a plain <see cref="HttpRequestException"/> — never an
/// <see cref="Contracts.ApiException"/> — so the paths that already degrade (cookie
/// re-validation's fail-open, the time-zone fallback, each controller's catch) keep working
/// without a change.
/// </para>
/// </summary>
public sealed class ApiResilienceHandler(
    ApiCircuitBreaker breaker,
    ApiResilienceOptions options,
    IHttpContextAccessor httpContextAccessor,
    ILogger<ApiResilienceHandler> logger) : DelegatingHandler
{
    /// <summary>Per-request fault marker, on HttpContext rather than the handler: HttpClient
    /// pool and reuse handler instances across requests, so the handler must hold no state.</summary>
    private static readonly object FaultItemKey = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // The page has already paid for one failed connection; the rest of its calls are free.
        if (httpContextAccessor.HttpContext?.Items[FaultItemKey] is HttpRequestException lost)
        {
            var repeated = new HttpRequestException(
                $"The API is unreachable ({lost.Message}); this request will not try again.",
                lost,
                HttpStatusCode.ServiceUnavailable);
            MarkRequestFailed(repeated);
            throw repeated;
        }

        if (!breaker.TryEnter(out var reason))
        {
            var rejected = new HttpRequestException(
                $"The API is unreachable; {reason}.",
                null,
                HttpStatusCode.ServiceUnavailable);
            MarkRequestFailed(rejected);
            throw rejected;
        }

        var response = await SendWithinBudgetAsync(request, cancellationToken);

        if (breaker.RecordSuccess())
        {
            logger.LogInformation("The API is answering again; leaving outage mode");
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendWithinBudgetAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var headerTimeout = new CancellationTokenSource(options.HeaderTimeout);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, headerTimeout.Token);

        HttpResponseMessage response;
        try
        {
            // ResponseHeadersRead so the budget covers only connect and headers. A body already
            // streaming — exam audio, an admin export — must be read to the end, however long.
            response = await base.SendAsync(request, linked.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Two different budgets can end the wait, and naming the wrong one sends whoever
            // reads the log after the wrong number: SocketsHttpHandler reports its own connect
            // timeout as a cancellation, so the header timer is only to blame if it is what fired.
            var budget = headerTimeout.IsCancellationRequested
                ? $"did not answer within {options.HeaderTimeout.TotalSeconds:F0}s"
                : $"could not be connected to within {options.ConnectTimeout.TotalSeconds:F0}s";
            Dispose(headerTimeout, linked);
            throw RecordTransportFault(new HttpRequestException($"The API {budget}.", ex));
        }
        catch (OperationCanceledException)
        {
            // The learner closed the tab mid-call. Not a fault of the API's, so the breaker
            // learns nothing and the probe stays available.
            Dispose(headerTimeout, linked);
            breaker.AbandonProbe();
            throw;
        }
        catch (HttpRequestException ex)
        {
            Dispose(headerTimeout, linked);
            throw RecordTransportFault(ex);
        }

        // Headers are in, so the timer must stop: disposing it is what stops it, and the linked
        // source is deliberately left alive so the body still observes the learner's abort.
        // shortcut: that source is reclaimed by the GC instead of being disposed, because
        // disposing it here would drop the abort registration the body stream depends on; the
        // request's own token source unregisters it when the request ends.
        Dispose(headerTimeout);

        return response;
    }

    private static void Dispose(params IDisposable[] disposables)
    {
        foreach (var disposable in disposables)
        {
            disposable.Dispose();
        }
    }

    private void MarkRequestFailed(HttpRequestException fault)
    {
        if (httpContextAccessor.HttpContext is { } context)
        {
            context.Items[FaultItemKey] = fault;
        }
    }

    private HttpRequestException RecordTransportFault(HttpRequestException fault)
    {
        MarkRequestFailed(fault);

        if (breaker.RecordTransportFailure())
        {
            logger.LogWarning(
                fault,
                "The API is unreachable; failing fast for {BreakSeconds}s",
                options.BreakDuration.TotalSeconds);
        }

        return fault;
    }
}
