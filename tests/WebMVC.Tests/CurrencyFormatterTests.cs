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

    [Theory]
    [InlineData(0, "0 ₫")]
    [InlineData(500000, "500,000 ₫")]
    [InlineData(20200000, "20.2M ₫")]
    [InlineData(56900000, "56.9M ₫")]
    [InlineData(109900000, "109.9M ₫")]
    public void FormatRevenue_formats_large_sums_compactly_with_M_suffix(decimal amount, string expected)
    {
        Assert.Equal(expected, CurrencyFormatter.FormatRevenue(amount));
    }
}
