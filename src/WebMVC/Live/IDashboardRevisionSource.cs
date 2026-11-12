using Contracts;

namespace WebMVC.Live;

/// <summary>
/// #286: where the web app gets the dashboard revision from. The browser never calls the API (ADR-0002),
/// so this is the one seam between the stream and the figures: production forwards to
/// <see cref="IAdminDashboardClient"/>, and the stream's tests substitute a stub they can change by hand.
/// <para>
/// It is an abstraction rather than a direct client call because the stream's behaviour under test is
/// "emit when this value changes, and not otherwise" - a claim that needs a value the test controls, not a
/// running API and a clock advanced in real time.
/// </para>
/// </summary>
public interface IDashboardRevisionSource
{
    Task<DashboardRevision> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Production: the revision is whatever the API says it is, forwarded under the caller's cookie.</summary>
public sealed class ApiDashboardRevisionSource(IAdminDashboardClient api, CurrentUser currentUser)
    : IDashboardRevisionSource
{
    public async Task<DashboardRevision> ReadAsync(CancellationToken cancellationToken = default)
    {
        var token = await currentUser.GetBearerTokenAsync();
        return await api.GetDashboardRevisionAsync(token ?? string.Empty, cancellationToken);
    }
}
