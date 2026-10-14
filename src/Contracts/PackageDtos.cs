namespace Contracts;

public sealed record PackageDto(
    int PackageId,
    string Name,
    int Credits,
    decimal Price,
    bool IsActive,
    bool IsFeatured,
    string? Features,
    int DisplayOrder,
    int OrdersSold = 0,
    decimal TotalRevenue = 0)
{
    public IReadOnlyList<string> FeatureList =>
        string.IsNullOrWhiteSpace(Features)
            ? []
            : Features.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed record CreatePackageRequest(
    string Name,
    int Credits,
    decimal Price,
    bool IsActive = true,
    bool IsFeatured = false,
    string? Features = null,
    int DisplayOrder = 0);

public sealed record UpdatePackageRequest(
    string Name,
    int Credits,
    decimal Price,
    bool IsActive,
    bool IsFeatured,
    string? Features = null,
    int DisplayOrder = 0);

public sealed record AdminPackagesSummaryDto(
    IReadOnlyList<PackageDto> Packages,
    int PackagesOnSale,
    int FeaturedCount,
    int TotalOrdersSold,
    decimal TotalRevenue);
