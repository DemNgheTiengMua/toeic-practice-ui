using Contracts;
namespace WebMVC.Models;
public sealed record AdminPackagesViewModel(AdminPackagesSummaryDto Summary)
{
    public string TotalRevenue { get; } = CurrencyFormatter.FormatRevenue(Summary.TotalRevenue);
    public string TotalAmount { get; } = CurrencyFormatter.FormatVnd(Summary.TotalRevenue);
    public IReadOnlyDictionary<int, string> Prices { get; } = Summary.Packages.ToDictionary(p => p.PackageId, p => CurrencyFormatter.FormatVnd(p.Price));
    public IReadOnlyDictionary<int, string> UnitPrices { get; } = Summary.Packages.ToDictionary(p => p.PackageId, p => CurrencyFormatter.FormatUnitPrice(p.Price, p.Credits));
    public IReadOnlyDictionary<int, string> Revenues { get; } = Summary.Packages.ToDictionary(p => p.PackageId, p => CurrencyFormatter.FormatRevenue(p.TotalRevenue));
}
