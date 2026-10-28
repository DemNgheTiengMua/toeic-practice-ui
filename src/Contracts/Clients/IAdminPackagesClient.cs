
namespace Contracts;

/// <summary>Where a media file is stored: under an import batch (ADR-0007) or under an exam (studio uploads).</summary>


public interface IAdminPackagesClient
{
    Task<AdminPackagesSummaryDto> GetAdminPackagesAsync(CancellationToken cancellationToken = default);
    Task<AdminPackagesSummaryDto> GetAdminPackagesAsync(string bearerToken, CancellationToken cancellationToken = default) => GetAdminPackagesAsync(cancellationToken);
    Task<PackageDto> GetAdminPackageAsync(int packageId, CancellationToken cancellationToken = default);
    Task<PackageDto> GetAdminPackageAsync(int packageId, string bearerToken, CancellationToken cancellationToken = default) => GetAdminPackageAsync(packageId, cancellationToken);
    Task<PackageDto> CreatePackageAsync(CreatePackageRequest request, CancellationToken cancellationToken = default);
    Task<PackageDto> CreatePackageAsync(CreatePackageRequest request, string bearerToken, CancellationToken cancellationToken = default) => CreatePackageAsync(request, cancellationToken);
    Task<PackageDto> UpdatePackageAsync(int packageId, UpdatePackageRequest request, CancellationToken cancellationToken = default);
    Task<PackageDto> UpdatePackageAsync(int packageId, UpdatePackageRequest request, string bearerToken, CancellationToken cancellationToken = default) => UpdatePackageAsync(packageId, request, cancellationToken);
    Task DeactivatePackageAsync(int packageId, CancellationToken cancellationToken = default);
    Task DeactivatePackageAsync(int packageId, string bearerToken, CancellationToken cancellationToken = default) => DeactivatePackageAsync(packageId, cancellationToken);
}
