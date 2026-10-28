
namespace Contracts;

public interface IAdminDashboardClient
{
    Task<AdminDashboardDto> GetAdminDashboardAsync(CancellationToken cancellationToken = default);
    Task<AdminDashboardDto> GetAdminDashboardAsync(string bearerToken, CancellationToken cancellationToken = default) => GetAdminDashboardAsync(cancellationToken);
}
