using Contracts;

namespace WebMVC.Models;

public sealed record MockGatewayViewModel(
    string OrderCode,
    string PackageName,
    int Credits,
    decimal Amount)
{
    public string AmountDisplay { get; } = CurrencyFormatter.FormatVnd(Amount);
}
