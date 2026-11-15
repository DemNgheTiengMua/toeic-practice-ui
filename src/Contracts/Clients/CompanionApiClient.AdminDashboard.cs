using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{

    /// <summary>
    /// #351: <paramref name="month"/> is any day of the month to report on; its day is ignored. Null asks
    /// for the month the server's clock is in, which is what every caller got before the parameter existed.
    /// </summary>
    public Task<AdminDashboardDto> GetAdminDashboardAsync(DateOnly? month, string bearerToken, CancellationToken cancellationToken = default)
    {
        var path = month is { } value
            ? $"api/admin/dashboard?month={Uri.EscapeDataString(value.ToString("yyyy-MM-dd"))}"
            : "api/admin/dashboard";
        return GetAuthorizedAsync<AdminDashboardDto>(path, bearerToken, cancellationToken);
    }

    public Task<AdminDashboardDto> GetAdminDashboardAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAdminDashboardAsync(null, bearerToken, cancellationToken);

    public Task<AdminDashboardDto> GetAdminDashboardAsync(DateOnly? month = null, CancellationToken cancellationToken = default) =>
        GetAdminDashboardAsync(month, string.Empty, cancellationToken);

    public Task<DashboardRevision> GetDashboardRevisionAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<DashboardRevision>("api/admin/dashboard/revision", bearerToken, cancellationToken);
}
