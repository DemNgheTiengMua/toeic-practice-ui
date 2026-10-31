using System.Globalization;

namespace WebMVC.Infrastructure;

/// <summary>
/// Budgets for calls from the MVC site to the API, all from configuration so they can be tuned
/// without a release. Defaults are sized for a local/loopback API: a refused connection on Windows
/// costs about 4 s because <c>localhost</c> resolves to both loopback addresses and each is tried,
/// so the connect phase is capped well below that.
/// </summary>
public sealed class ApiResilienceOptions
{
    public const string SectionName = "ApiResilience";

    /// <summary>Upper bound on establishing the TCP connection. Refused or filtered connections
    /// end here instead of at the OS default.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Upper bound on waiting for response headers. Generous enough for the slowest
    /// legitimate call (admin exports and imports). Never applies to a body already streaming.</summary>
    public TimeSpan HeaderTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Consecutive transport faults, across requests, that open the breaker.</summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>How long the breaker stays open before letting one call through to probe.</summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(10);

    public static ApiResilienceOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        var options = new ApiResilienceOptions();

        // Milliseconds in configuration: a TimeSpan key would need a format a hand-edited
        // appsettings.json gets wrong silently, and a wrong budget here is hard to notice.
        if (int.TryParse(section["ConnectTimeoutMs"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var connect) && connect > 0)
        {
            options.ConnectTimeout = TimeSpan.FromMilliseconds(connect);
        }

        if (int.TryParse(section["HeaderTimeoutMs"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var header) && header > 0)
        {
            options.HeaderTimeout = TimeSpan.FromMilliseconds(header);
        }

        if (int.TryParse(section["FailureThreshold"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var threshold) && threshold > 0)
        {
            options.FailureThreshold = threshold;
        }

        if (int.TryParse(section["BreakDurationMs"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var breakMs) && breakMs > 0)
        {
            options.BreakDuration = TimeSpan.FromMilliseconds(breakMs);
        }

        return options;
    }
}
