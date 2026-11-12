
namespace Contracts;

public interface IAdminDashboardClient
{
    Task<AdminDashboardDto> GetAdminDashboardAsync(CancellationToken cancellationToken = default);
    Task<AdminDashboardDto> GetAdminDashboardAsync(string bearerToken, CancellationToken cancellationToken = default) => GetAdminDashboardAsync(cancellationToken);

    /// <summary>#286: the revision the live stream watches. Equality is the whole contract.</summary>
    Task<DashboardRevision> GetDashboardRevisionAsync(string bearerToken, CancellationToken cancellationToken = default);
}
