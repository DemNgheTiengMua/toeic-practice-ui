using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

public sealed partial class CompanionApiClient
{

    public Task<AdminPackagesSummaryDto> GetAdminPackagesAsync(string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<AdminPackagesSummaryDto>("api/admin/packages", bearerToken, cancellationToken);

    public Task<PackageDto> GetAdminPackageAsync(int packageId, string bearerToken, CancellationToken cancellationToken = default) =>
        GetAuthorizedAsync<PackageDto>($"api/admin/packages/{packageId}", bearerToken, cancellationToken);

    public async Task<PackageDto> CreatePackageAsync(CreatePackageRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/admin/packages")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<PackageDto>(Json, cancellationToken))!;
    }

    public async Task<PackageDto> UpdatePackageAsync(int packageId, UpdatePackageRequest request, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, $"api/admin/packages/{packageId}")
        {
            Content = JsonContent.Create(request, options: Json),
        };
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<PackageDto>(Json, cancellationToken))!;
    }

    public async Task DeactivatePackageAsync(int packageId, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"api/admin/packages/{packageId}/deactivate");
        AttachBearer(message, bearerToken);
        using var response = await http.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public Task<AdminPackagesSummaryDto> GetAdminPackagesAsync(CancellationToken cancellationToken = default) =>
        GetAdminPackagesAsync(string.Empty, cancellationToken);

    public Task<PackageDto> GetAdminPackageAsync(int packageId, CancellationToken cancellationToken = default) =>
        GetAdminPackageAsync(packageId, string.Empty, cancellationToken);

    public Task<PackageDto> CreatePackageAsync(CreatePackageRequest request, CancellationToken cancellationToken = default) =>
        CreatePackageAsync(request, string.Empty, cancellationToken);

    public Task<PackageDto> UpdatePackageAsync(int packageId, UpdatePackageRequest request, CancellationToken cancellationToken = default) =>
        UpdatePackageAsync(packageId, request, string.Empty, cancellationToken);

    public Task DeactivatePackageAsync(int packageId, CancellationToken cancellationToken = default) =>
        DeactivatePackageAsync(packageId, string.Empty, cancellationToken);
}
