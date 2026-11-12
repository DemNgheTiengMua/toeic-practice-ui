using System.Globalization;

namespace Contracts;

/// <summary>
/// Renders monetary amounts in Vietnamese Dong (VND) according to UI specifications.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every figure here is exact.</b> An amount on screen must equal the amount in the database; a
/// formatter that rounded, abbreviated or grouped its way to a different number would make a financial
/// screen wrong by an amount the reader cannot see. <c>FormatRevenue</c> used to render 20,249,999 as
/// "20.2M", which is 49,999 short, and it was the figure on the admin dashboard.
/// </para>
/// <para>
/// <b>Sub-dong amounts keep their places.</b> The schema stores money as <c>DECIMAL(12,2)</c>, and a
/// refund or a share of a package can carry a fraction. Two decimal places are shown only when they are
/// non-zero, so a whole amount reads "199,000 ₫" and a fractional one reads "1,234.56 ₫" rather than
/// either hiding the fraction or padding every price with ".00".
/// </para>
/// <para>
/// <b>The sign is a true minus sign (U+2212), not a hyphen.</b> A hyphen is a dash at text size and reads
/// as decoration; the minus glyph is the arithmetic operator a negative revenue is (#284). It sits outside
/// the grouped digits so "-500,000" cannot be mistaken for a dash-prefixed positive.
/// </para>
/// </remarks>
public static class CurrencyFormatter
{
    private static readonly CultureInfo UsCulture = CultureInfo.InvariantCulture;

    /// <summary>A whole amount carries no decimal point at all.</summary>
    private const string WholePattern = "#,##0";

    /// <summary>A fractional amount keeps both stored places, so "0.50" never renders as "0.5".</summary>
    private const string FractionalPattern = "#,##0.00";

    private const char TrueMinus = '\u2212';

    public static string FormatVnd(decimal amount) =>
        $"{Signed(amount)} ₫";

    public static string FormatUnitPrice(decimal price, int credits)
    {
        if (credits <= 0)
        {
            return $"{FormatVnd(price)}/credit";
        }

        // A unit price is a rate, not an amount owed: it is not payable and is never summed, so it is
        // stated to the dong. The division is decimal throughout, so no binary rounding creeps in.
        var unitPrice = Math.Round(price / credits, 0, MidpointRounding.AwayFromZero);
        return $"{Signed(unitPrice)} ₫/credit";
    }

    /// <summary>
    /// The exact amount, unchanged. Kept as its own name because revenue is read as a total in a card
    /// while a price is read as a line item, and a future presentation change should be able to affect one
    /// without the other — but neither may ever state a number that differs from the stored one.
    /// </summary>
    public static string FormatRevenue(decimal amount) => FormatVnd(amount);

    private static string Signed(decimal amount)
    {
        var magnitude = Math.Abs(amount);
        var digits = magnitude.ToString(
            decimal.Truncate(magnitude) == magnitude ? WholePattern : FractionalPattern,
            UsCulture);

        return amount < 0 ? $"{TrueMinus}{digits}" : digits;
    }
}
