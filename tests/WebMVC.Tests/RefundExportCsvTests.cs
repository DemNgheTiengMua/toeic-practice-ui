using System.Text;
using Contracts;

namespace WebMVC.Tests;

/// <summary>
/// #283: the accounting export. These assert the bytes a spreadsheet receives, not how they were built:
/// the header a producer reads, the diacritics that must survive, the quoting RFC 4180 requires, and the
/// byte-order mark without which Excel opens a UTF-8 file as mojibake on a Vietnamese Windows install.
/// <para>
/// The formatter is a pure function over rows, so nothing here needs a database or a request.
/// </para>
/// </summary>
public sealed class RefundExportCsvTests
{
    private static readonly DateTimeOffset ExportedAt = new(2026, 11, 12, 3, 30, 0, TimeSpan.Zero);

    private const string Header =
        "Refund ID,Requested on,Settled on,Learner email,Learner name,Order code,Method,Money amount,Credit count,Reason,Decided by,Exported at";

    private static RefundExportRow MoneyRow() => new(
        RefundId: 27,
        RequestedOn: new DateOnly(2026, 10, 1),
        SettledOn: new DateOnly(2026, 10, 5),
        LearnerEmail: "lan@test.local",
        LearnerName: "Nguyễn Thị Lan",
        OrderCode: "DH-8802",
        Method: RefundMethodDto.Money,
        Amount: 150000m,
        CreditCount: null,
        Reason: "Hoàn tiền do mentor không đến",
        DecidingAdmin: "Trần Văn Minh");

    private static RefundExportRow CreditRow() => new(
        RefundId: 31,
        RequestedOn: new DateOnly(2026, 10, 2),
        SettledOn: null,
        LearnerEmail: "a@test.local",
        LearnerName: "Lê Minh C",
        OrderCode: null,
        Method: RefundMethodDto.Credit,
        Amount: null,
        CreditCount: 5,
        Reason: "Chấm bài sai",
        DecidingAdmin: null);

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes[3..]);

    /// <summary>RFC 4180 records. A quoted field can contain a line break, so splitting on CRLF is only
    /// sound for the plain rows these tests build.</summary>
    private static string[] Lines(byte[] bytes) =>
        Text(bytes).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void The_file_begins_with_a_byte_order_mark()
    {
        var bytes = RefundExportCsv.Format([MoneyRow()], ExportedAt);

        // Three bytes, not a U+FEFF character: Excel sniffs the bytes, and a text column that starts
        // with the BOM bytes is how a UTF-8 CSV opens with its diacritics intact.
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
    }

    [Fact]
    public void The_header_is_exactly_the_agreed_columns_in_order()
    {
        var lines = Lines(RefundExportCsv.Format([], ExportedAt));

        Assert.Equal(Header, Assert.Single(lines));
    }

    [Fact]
    public void An_empty_range_is_a_header_and_nothing_else()
    {
        var bytes = RefundExportCsv.Format([], ExportedAt);

        Assert.Equal(Header + "\r\n", Text(bytes));
    }

    [Fact]
    public void A_vietnamese_name_and_reason_survive_the_round_trip()
    {
        var bytes = RefundExportCsv.Format([MoneyRow()], ExportedAt);
        var row = Lines(bytes)[1];

        // Re-decoding the exact bytes is the assertion: a formatter that lost a diacritic would still
        // produce a well-formed file, and no other test would notice.
        Assert.Contains("Nguyễn Thị Lan", row, StringComparison.Ordinal);
        Assert.Contains("Hoàn tiền do mentor không đến", row, StringComparison.Ordinal);
        Assert.Contains("Trần Văn Minh", row, StringComparison.Ordinal);
    }

    [Fact]
    public void A_money_row_fills_the_amount_and_leaves_the_credit_count_empty()
    {
        var columns = Lines(RefundExportCsv.Format([MoneyRow()], ExportedAt))[1].Split(',');

        // A bare number, not a display string: accounting sums this column in a spreadsheet, and
        // "150,000 ₫" would arrive as text or as two cells.
        Assert.Equal("150000.00", columns[7]);
        Assert.Equal("", columns[8]);

        // The dates are the local reporting dates, ISO-8601, so Excel reads a date in any locale.
        Assert.Equal("2026-10-01", columns[1]);
        Assert.Equal("2026-10-05", columns[2]);
        Assert.Equal("money", columns[6]);
    }

    [Fact]
    public void A_credit_row_fills_the_credit_count_and_leaves_the_amount_empty()
    {
        var columns = Lines(RefundExportCsv.Format([CreditRow()], ExportedAt))[1].Split(',');

        Assert.Equal("", columns[7]);
        Assert.Equal("5", columns[8]);

        // An unsettled credit refund never carries a settlement date, and an orderless one has no code.
        Assert.Equal("", columns[2]);
        Assert.Equal("", columns[5]);
        Assert.Equal("credit", columns[6]);
    }

    [Fact]
    public void A_field_with_a_comma_a_quote_or_a_line_break_is_quoted_and_its_quotes_doubled()
    {
        var awkward = MoneyRow() with
        {
            Reason = "Sai đề, \"phần nghe\" bị lỗi\r\nhọc viên phàn nàn",
        };

        // Asserted over the whole text, not a line: the reason itself contains the line break the
        // record separator uses, which is exactly the case a naive split would file as two records.
        var text = Text(RefundExportCsv.Format([awkward], ExportedAt));

        // RFC 4180: quote the field, double an embedded quote. Without this the file has one cell per
        // comma and a reason becomes the header's neighbour.
        Assert.Contains("\"Sai đề, \"\"phần nghe\"\" bị lỗi\r\nhọc viên phàn nàn\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_row_records_when_the_export_ran()
    {
        var rows = Lines(RefundExportCsv.Format([MoneyRow(), CreditRow()], ExportedAt)).Skip(1).ToList();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.EndsWith(",2026-11-12 03:30", row, StringComparison.Ordinal));
    }
}
