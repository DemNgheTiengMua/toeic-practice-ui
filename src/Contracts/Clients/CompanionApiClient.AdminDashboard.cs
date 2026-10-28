using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{

    public Task<AdminDashboardDto> GetAdminDashboardAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AdminDashboardDto>("api/admin/dashboard", bearerToken, cancellationToken);

    public Task<AdminDashboardDto> GetAdminDashboardAsync(CancellationToken cancellationToken = default) =>
        GetAdminDashboardAsync(string.Empty, cancellationToken);
}
