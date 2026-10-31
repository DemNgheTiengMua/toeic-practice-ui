namespace WebMVC.Infrastructure;

/// <summary>
/// Process-wide view of whether the API is reachable, shared by every request so an outage costs
/// one failed connection in total rather than one per page view.
/// <para>
/// Closed: calls go through; consecutive transport faults count towards the threshold.
/// Open: calls are rejected immediately, without touching the network, until the break expires.
/// Half-open (one probe allowed): a single call is let through; success closes the breaker (and
/// resets the counter), failure re-opens it for another full break.
/// </para>
/// </summary>
public sealed class ApiCircuitBreaker(ApiResilienceOptions options, TimeProvider time)
{
    private readonly Lock gate = new();
    private int consecutiveFailures;
    private DateTimeOffset? openedAt;
    private bool probeInFlight;

    /// <summary>True while the breaker is refusing calls; the caller should throw without sending.</summary>
    public bool TryEnter(out string reason)
    {
        lock (gate)
        {
            if (openedAt is not { } opened)
            {
                reason = "";
                return true;
            }

            var elapsed = time.GetUtcNow() - opened;
            if (elapsed < options.BreakDuration)
            {
                reason = $"the API has been unreachable for {elapsed.TotalSeconds:F0}s; " +
                         $"retrying in {(options.BreakDuration - elapsed).TotalSeconds:F0}s";
                return false;
            }

            // Break expired: let exactly one caller through to find out whether the API is back.
            if (probeInFlight)
            {
                reason = "a probe for the API is already in flight";
                return false;
            }

            probeInFlight = true;
            reason = "";
            return true;
        }
    }

    /// <summary>A call reached the API and was answered — whatever the status. Closes the breaker.
    /// Returns true when this ended an outage, so the caller logs one line exactly there.</summary>
    public bool RecordSuccess()
    {
        lock (gate)
        {
            var wasOpen = openedAt is not null;
            consecutiveFailures = 0;
            openedAt = null;
            probeInFlight = false;
            return wasOpen;
        }
    }

    /// <summary>A call could not reach the API at all. Opens the breaker once the run is long enough.
    /// Returns true only on the call that opened it.</summary>
    public bool RecordTransportFailure()
    {
        lock (gate)
        {
            probeInFlight = false;
            consecutiveFailures++;
            if (consecutiveFailures < options.FailureThreshold || openedAt is not null)
            {
                return false;
            }

            openedAt = time.GetUtcNow();
            return true;
        }
    }

    /// <summary>Ends a probe that was abandoned (client abort) without judging the API either way.</summary>
    public void AbandonProbe()
    {
        lock (gate)
        {
            probeInFlight = false;
        }
    }

    /// <summary>For tests and diagnostics: whether the breaker is currently refusing calls.</summary>
    public bool IsOpen
    {
        get
        {
            lock (gate)
            {
                return openedAt is { } opened && time.GetUtcNow() - opened < options.BreakDuration;
            }
        }
    }
}
