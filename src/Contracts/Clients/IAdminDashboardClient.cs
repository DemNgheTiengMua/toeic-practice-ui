
namespace Contracts;

public interface IAdminDashboardClient
{
    /// <summary>
    /// #351: <paramref name="month"/> is any day of the month to report on; its day is ignored. Null asks
    /// for the month the server's clock is in, which is what every caller got before it existed.
    /// </summary>
    Task<AdminDashboardDto> GetAdminDashboardAsync(DateOnly? month = null, CancellationToken cancellationToken = default);
    Task<AdminDashboardDto> GetAdminDashboardAsync(DateOnly? month, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAdminDashboardAsync(month, cancellationToken);

    /// <summary>#286: the revision the live stream watches. Equality is the whole contract.</summary>
    Task<DashboardRevision> GetDashboardRevisionAsync(string bearerToken, CancellationToken cancellationToken = default);
}
