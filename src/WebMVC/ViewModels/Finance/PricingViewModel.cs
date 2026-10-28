using Contracts;
namespace WebMVC.Models;
public sealed record PricingViewModel(IReadOnlyList<PackageDto> Packages, bool IsAdmin, bool HasLearnerShell, int CreditBalance)
{
    public IReadOnlyDictionary<int, string> Prices { get; } = Packages.ToDictionary(p => p.PackageId, p => CurrencyFormatter.FormatVnd(p.Price));
    public IReadOnlyDictionary<int, string> UnitPrices { get; } = Packages.ToDictionary(p => p.PackageId, p => CurrencyFormatter.FormatUnitPrice(p.Price, p.Credits));
}
