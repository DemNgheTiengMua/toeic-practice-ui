using Contracts;

namespace WebMVC.Tests;

public sealed class CurrencyFormatterTests
{
    [Theory]
    [InlineData(0, "0 ₫")]
    [InlineData(49000, "49,000 ₫")]
    [InlineData(199000, "199,000 ₫")]
    [InlineData(349000, "349,000 ₫")]
    [InlineData(1000000, "1,000,000 ₫")]
    public void FormatVnd_formats_amounts_with_thousands_separator_and_dong_symbol(decimal amount, string expected)
    {
        Assert.Equal(expected, CurrencyFormatter.FormatVnd(amount));
    }

    [Theory]
    [InlineData(49000, 1, "49,000 ₫/credit")]
    [InlineData(199000, 5, "39,800 ₫/credit")]
    [InlineData(349000, 10, "34,900 ₫/credit")]
    public void FormatUnitPrice_calculates_and_formats_unit_price(decimal price, int credits, string expected)
    {
        Assert.Equal(expected, CurrencyFormatter.FormatUnitPrice(price, credits));
    }

    // Money is never abbreviated. "20.2M" reads as 20,200,000 and is 49,999 short of 20,249,999, so a
    // revenue figure that rounded would be wrong by an amount a reader cannot see. Every case here is
    // exact to the dong, and each pair differs in a digit the old compact form discarded.
    [Theory]
    [InlineData(0, "0 ₫")]
    [InlineData(500000, "500,000 ₫")]
    [InlineData(999999, "999,999 ₫")]
    [InlineData(1000000, "1,000,000 ₫")]
    [InlineData(20200000, "20,200,000 ₫")]
    [InlineData(20249999, "20,249,999 ₫")]
    [InlineData(56900000, "56,900,000 ₫")]
    [InlineData(109949999, "109,949,999 ₫")]
    public void FormatRevenue_states_the_exact_amount_never_an_abbreviation(decimal amount, string expected)
    {
        Assert.Equal(expected, CurrencyFormatter.FormatRevenue(amount));
    }

    // A refund can push a day's net revenue below zero (#284). The minus must be U+2212 and must sit
    // outside the grouping, or the figure reads as a dash-prefixed positive.
    [Theory]
    [InlineData(-1, "\u22121 ₫")]
    [InlineData(-500000, "\u2212500,000 ₫")]
    [InlineData(-20249999, "\u221220,249,999 ₫")]
    public void FormatRevenue_renders_a_negative_amount_with_a_true_minus_sign(decimal amount, string expected)
    {
        Assert.Equal(expected, CurrencyFormatter.FormatRevenue(amount));
    }

    [Theory]
    [InlineData(0.5, "0.50 ₫")]
    [InlineData(1234.56, "1,234.56 ₫")]
    [InlineData(199000.00, "199,000 ₫")]
    public void FormatVnd_keeps_the_two_decimal_places_the_schema_stores(decimal amount, string expected)
    {
        Assert.Equal(expected, CurrencyFormatter.FormatVnd(amount));
    }

    [Fact]
    public void FormatVnd_never_loses_a_dong_of_a_stored_amount()
    {
        // Every amount the schema can hold is DECIMAL(12,2). Formatting then re-parsing must return the
        // same value, or a figure on screen is not the figure in the database.
        foreach (var amount in new[] { 0m, 0.01m, 0.5m, 1234.56m, 999999.99m, 1000000000.00m })
        {
            var text = CurrencyFormatter.FormatVnd(amount).Replace(" ₫", "");
            var parsed = decimal.Parse(text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(amount, parsed);
        }
    }
}
