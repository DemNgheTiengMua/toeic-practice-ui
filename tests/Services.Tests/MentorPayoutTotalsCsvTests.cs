using System.Text;
using Contracts;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #353: the second accounting file. One row per mentor, so a transfer needs no arithmetic outside the
/// product. Same contract as the per-session export - UTF-8 BOM, RFC 4180 quoting, ISO dates, bare money -
/// because accounting opens both in the same spreadsheet.
/// </summary>
public sealed class MentorPayoutTotalsCsvTests
{
    [Fact]
    public void Format_PrependsUtf8Bom_AndFormatsVietnameseDiacriticsAndMoney()
    {
        var exportedAt = new DateTimeOffset(2026, 11, 14, 15, 30, 0, TimeSpan.FromHours(7));
        var rows = new[]
        {
            new MentorPayoutTotalsRow(
                MentorId: 7,
                MentorName: "Dr. Trần Minh Khoa",
                CertCode: "TOEIC",
                SessionCount: 3,
                TotalCredits: 6,
                TotalAmountVnd: 120_000m),
        };

        var bytes = MentorPayoutTotalsCsv.Format(rows, exportedAt);

        // UTF-8 BOM: EF BB BF, without which Excel on a Vietnamese install decodes the names as ANSI.
        Assert.True(bytes.Length > 3);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);

        var csv = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Equal(MentorPayoutTotalsCsv.Header, lines[0]);
        Assert.Equal(
            "7,Dr. Trần Minh Khoa,TOEIC,3,6,120000.00,2026-11-14 15:30",
            lines[1]);
    }

    [Fact]
    public void Format_QuotesFieldsContainingCommasQuotesOrNewlines_PerRfc4180()
    {
        var exportedAt = new DateTimeOffset(2026, 11, 14, 9, 5, 0, TimeSpan.FromHours(7));
        var rows = new[]
        {
            new MentorPayoutTotalsRow(
                MentorId: 3,
                MentorName: "Mentor, \"Special\" Coach",
                CertCode: "IELTS",
                SessionCount: 1,
                TotalCredits: 1,
                TotalAmountVnd: 20_000m),
        };

        var bytes = MentorPayoutTotalsCsv.Format(rows, exportedAt);
        var csv = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        // An unquoted comma in a mentor's name would shift every figure one column to the right.
        Assert.Contains("\"Mentor, \"\"Special\"\" Coach\"", csv);
        Assert.Contains(",IELTS,1,1,20000.00,", csv);
    }

    [Fact]
    public void Format_WithNoDeliveredSessions_ReturnsTheHeaderOnly_NotAnError()
    {
        var exportedAt = new DateTimeOffset(2026, 11, 14, 9, 5, 0, TimeSpan.FromHours(7));

        var bytes = MentorPayoutTotalsCsv.Format([], exportedAt);

        var csv = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.Equal(MentorPayoutTotalsCsv.Header + "\r\n", csv);
    }
}
