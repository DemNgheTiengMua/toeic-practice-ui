using System.Globalization;

namespace Contracts;

/// <summary>
/// Renders monetary amounts in Vietnamese Dong (VND) according to UI specifications.
/// Prices render as "199,000 ₫", unit prices as "39,800 ₫/credit",
/// and high revenue values as compact "109.9M ₫".
/// </summary>
public static class CurrencyFormatter
{
    private static readonly CultureInfo UsCulture = CultureInfo.InvariantCulture;

    public static string FormatVnd(decimal amount) =>
        $"{amount.ToString("#,##0", UsCulture)} ₫";

    public static string FormatUnitPrice(decimal price, int credits)
    {
        if (credits <= 0)
        {
            return $"{FormatVnd(price)}/credit";
        }

        var unitPrice = Math.Round(price / credits, 0, MidpointRounding.AwayFromZero);
        return $"{unitPrice.ToString("#,##0", UsCulture)} ₫/credit";
    }

    public static string FormatRevenue(decimal amount)
    {
        if (amount >= 1_000_000m)
        {
            var millions = amount / 1_000_000m;
            return $"{millions.ToString("0.#", UsCulture)}M ₫";
        }

        return FormatVnd(amount);
    }
}
