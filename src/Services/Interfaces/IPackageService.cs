using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public interface IPackageService
{
    Task<IReadOnlyList<PackageDto>> GetActivePackagesAsync(CancellationToken cancellationToken = default);
    Task<AdminPackagesSummaryDto> GetAdminPackagesAsync(CancellationToken cancellationToken = default);
    Task<PackageDto?> GetPackageByIdAsync(int packageId, CancellationToken cancellationToken = default);
    Task<PackageDto> CreatePackageAsync(CreatePackageRequest request, CancellationToken cancellationToken = default);
    Task<PackageDto> UpdatePackageAsync(int packageId, UpdatePackageRequest request, CancellationToken cancellationToken = default);
    Task DeactivatePackageAsync(int packageId, CancellationToken cancellationToken = default);
}
