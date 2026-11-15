using System.Text;
using Contracts;
using Xunit;

namespace Services.Tests;

/// <summary>
/// The three accounting files - refunds, per-session payouts, per-mentor totals - promise accounting one
/// contract: UTF-8 with a byte-order mark, CRLF between records, RFC 4180 quoting, ISO-8601 dates and bare
/// decimal money. That promise used to be a private copy of the same twenty lines in each formatter, so a
/// fix to quoting reached one file and not the others. It is now one writer, and this is where the
/// contract is asserted rather than in whichever formatter happened to be edited last.
/// </summary>
public sealed class Rfc4180CsvTests
{
    [Fact]
    public void A_file_starts_with_the_bom_so_excel_reads_the_diacritics()
    {
        var bytes = Rfc4180Csv.Format("A,B", [["Trần", "1"]]);

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray());
        Assert.Equal("A,B\r\nTrần,1\r\n", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
    }

    [Fact]
    public void No_rows_yields_the_header_and_nothing_else()
    {
        // An empty file accounting cannot open is worse than one that states there is nothing to pay.
        var bytes = Rfc4180Csv.Format("A,B", []);

        Assert.Equal("A,B\r\n", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("has,comma", "\"has,comma\"")]
    [InlineData("has\"quote", "\"has\"\"quote\"")]
    [InlineData("has\r\ncrlf", "\"has\r\ncrlf\"")]
    [InlineData("", "")]
    public void A_field_is_quoted_only_when_it_has_to_be(string field, string expected)
    {
        var bytes = Rfc4180Csv.Format("H", [[field]]);

        // The header line ends at the first CRLF; the row is everything after it.
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.Equal(expected + "\r\n", text["H\r\n".Length..]);
    }

    [Fact]
    public void The_export_stamp_is_iso_shaped_in_invariant_culture()
    {
        // A bare time-of-day or a locale-shaped date is misread in the other locale, and a payment filed
        // in the wrong month is worse than one with no date. This is the one place the shape is decided.
        var stamp = Rfc4180Csv.ExportStamp(
            new DateTimeOffset(2026, 11, 14, 15, 30, 0, TimeSpan.FromHours(7)));

        Assert.Equal("2026-11-14 15:30", stamp);
    }
}
