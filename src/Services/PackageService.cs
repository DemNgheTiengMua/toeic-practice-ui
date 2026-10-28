using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed class PackageService(CompanionDbContext db) : IPackageService
{
    public async Task<IReadOnlyList<PackageDto>> GetActivePackagesAsync(CancellationToken cancellationToken = default)
    {
        return await db.Packages
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.PackageId)
            .Select(p => new PackageDto(
                p.PackageId,
                p.Name,
                p.Credits,
                p.Price,
                p.IsActive,
                p.IsFeatured,
                p.Features,
                p.DisplayOrder,
                0,
                0))
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminPackagesSummaryDto> GetAdminPackagesAsync(CancellationToken cancellationToken = default)
    {
        var packages = await db.Packages
            .AsNoTracking()
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.PackageId)
            .Select(p => new
            {
                p.PackageId,
                p.Name,
                p.Credits,
                p.Price,
                p.IsActive,
                p.IsFeatured,
                p.Features,
                p.DisplayOrder,
                OrdersSold = p.Orders.Count(o => o.Status == OrderStatus.Paid.ToCode()),
                TotalRevenue = p.Orders.Where(o => o.Status == OrderStatus.Paid.ToCode()).Sum(o => (decimal?)o.Amount) ?? 0m,
            })
            .ToListAsync(cancellationToken);

        var dtos = packages.Select(p => new PackageDto(
            p.PackageId,
            p.Name,
            p.Credits,
            p.Price,
            p.IsActive,
            p.IsFeatured,
            p.Features,
            p.DisplayOrder,
            p.OrdersSold,
            p.TotalRevenue)).ToList();

        var packagesOnSale = dtos.Count(p => p.IsActive);
        var featuredCount = dtos.Count(p => p.IsFeatured);
        var totalOrdersSold = dtos.Sum(p => p.OrdersSold);
        var totalRevenue = dtos.Sum(p => p.TotalRevenue);

        return new AdminPackagesSummaryDto(
            dtos,
            packagesOnSale,
            featuredCount,
            totalOrdersSold,
            totalRevenue);
    }

    public async Task<PackageDto?> GetPackageByIdAsync(int packageId, CancellationToken cancellationToken = default)
    {
        return await db.Packages
            .AsNoTracking()
            .Where(p => p.PackageId == packageId)
            .Select(p => new PackageDto(
                p.PackageId,
                p.Name,
                p.Credits,
                p.Price,
                p.IsActive,
                p.IsFeatured,
                p.Features,
                p.DisplayOrder,
                p.Orders.Count(o => o.Status == OrderStatus.Paid.ToCode()),
                p.Orders.Where(o => o.Status == OrderStatus.Paid.ToCode()).Sum(o => (decimal?)o.Amount) ?? 0m))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PackageDto> CreatePackageAsync(CreatePackageRequest request, CancellationToken cancellationToken = default)
    {
        ValidateInput(request.Name, request.Credits, request.Price);

        var isFeatured = request.IsActive && request.IsFeatured;
        if (isFeatured)
        {
            // Only one featured package at a time: unfeature any currently featured package first
            await db.Packages
                .Where(p => p.IsFeatured)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsFeatured, false), cancellationToken);
        }

        var entity = new Package
        {
            Name = request.Name.Trim(),
            Credits = request.Credits,
            Price = request.Price,
            IsActive = request.IsActive,
            IsFeatured = isFeatured,
            Features = request.Features?.Trim(),
            DisplayOrder = request.DisplayOrder,
        };

        db.Packages.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return new PackageDto(
            entity.PackageId,
            entity.Name,
            entity.Credits,
            entity.Price,
            entity.IsActive,
            entity.IsFeatured,
            entity.Features,
            entity.DisplayOrder);
    }

    public async Task<PackageDto> UpdatePackageAsync(int packageId, UpdatePackageRequest request, CancellationToken cancellationToken = default)
    {
        var package = await db.Packages.FirstOrDefaultAsync(p => p.PackageId == packageId, cancellationToken);
        if (package is null)
        {
            throw new KeyNotFoundException($"Package {packageId} was not found.");
        }

        ValidateInput(request.Name, request.Credits, request.Price);

        var isFeatured = request.IsActive && request.IsFeatured;
        if (isFeatured)
        {
            // Only one featured package at a time: unfeature any other package first
            await db.Packages
                .Where(p => p.IsFeatured && p.PackageId != packageId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsFeatured, false), cancellationToken);
        }

        package.Name = request.Name.Trim();
        package.Credits = request.Credits;
        package.Price = request.Price;
        package.IsActive = request.IsActive;
        package.IsFeatured = isFeatured;
        package.Features = request.Features?.Trim();
        package.DisplayOrder = request.DisplayOrder;

        await db.SaveChangesAsync(cancellationToken);

        return new PackageDto(
            package.PackageId,
            package.Name,
            package.Credits,
            package.Price,
            package.IsActive,
            package.IsFeatured,
            package.Features,
            package.DisplayOrder);
    }

    public async Task DeactivatePackageAsync(int packageId, CancellationToken cancellationToken = default)
    {
        var package = await db.Packages.FirstOrDefaultAsync(p => p.PackageId == packageId, cancellationToken);
        if (package is null)
        {
            throw new KeyNotFoundException($"Package {packageId} was not found.");
        }

        package.IsActive = false;
        package.IsFeatured = false; // Always clear featured slot when deactivating so unique constraint is freed
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateInput(string name, int credits, decimal price)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Package name is required.", nameof(name));
        }

        if (name.Length > 120)
        {
            throw new ArgumentException("Package name cannot exceed 120 characters.", nameof(name));
        }

        if (credits <= 0)
        {
            throw new ArgumentException("Credits must be greater than 0.", nameof(credits));
        }

        if (price < 0)
        {
            throw new ArgumentException("Price must be non-negative.", nameof(price));
        }
    }
}
