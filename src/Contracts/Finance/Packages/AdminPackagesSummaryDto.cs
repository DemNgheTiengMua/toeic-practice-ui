
namespace Contracts;

public sealed record AdminPackagesSummaryDto(
    IReadOnlyList<PackageDto> Packages,
    int PackagesOnSale,
    int FeaturedCount,
    int TotalOrdersSold,
    decimal TotalRevenue);
