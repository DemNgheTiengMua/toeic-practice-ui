using System.Text;
using Contracts;
using Xunit;

namespace Services.Tests;

public sealed class MentorPayoutExportCsvTests
{
    [Fact]
    public void Format_PrependsUtf8Bom_AndFormatsVietnameseDiacriticsAndMoney()
    {
        var exportedAt = new DateTimeOffset(2026, 11, 14, 15, 30, 0, TimeSpan.FromHours(7));
        var rows = new[]
        {
            new MentorPayoutExportRow(
                BookingId: 42,
                SessionDate: new DateOnly(2026, 11, 10),
                StartTime: new TimeOnly(9, 0),
                EndTime: new TimeOnly(10, 0),
                MentorId: 7,
                MentorName: "Dr. Trần Minh Khoa",
                CertCode: "TOEIC",
                SkillName: "Listening",
                LearnerEmail: "learner@demo.local",
                LearnerName: "Nguyễn Thị Lan",
                PriceCredits: 2,
                AmountVnd: 40_000m),
        };

        var bytes = MentorPayoutExportCsv.Format(rows, exportedAt);

        // UTF-8 BOM: EF BB BF
        Assert.True(bytes.Length > 3);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);

        var csv = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Equal(MentorPayoutExportCsv.Header, lines[0]);
        Assert.Equal(
            "42,2026-11-10,09:00,10:00,7,Dr. Trần Minh Khoa,TOEIC,Listening,learner@demo.local,Nguyễn Thị Lan,2,40000.00,2026-11-14 15:30",
            lines[1]);
    }

    [Fact]
    public void Format_QuotesFieldsContainingCommasQuotesOrNewlines_PerRfc4180()
    {
        var exportedAt = new DateTimeOffset(2026, 11, 14, 9, 5, 0, TimeSpan.FromHours(7));
        var rows = new[]
        {
            new MentorPayoutExportRow(
                BookingId: 99,
                SessionDate: new DateOnly(2026, 11, 12),
                StartTime: new TimeOnly(19, 0),
                EndTime: new TimeOnly(20, 0),
                MentorId: 3,
                MentorName: "Mentor, \"Special\" Coach",
                CertCode: "IELTS",
                SkillName: null,
                LearnerEmail: "test@demo.local",
                LearnerName: "Line1\nLine2",
                PriceCredits: 1,
                AmountVnd: 20_000m),
        };

        var bytes = MentorPayoutExportCsv.Format(rows, exportedAt);
        var csv = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        Assert.Contains("\"Mentor, \"\"Special\"\" Coach\"", csv);
        Assert.Contains("\"Line1\nLine2\"", csv);
        Assert.Contains(",IELTS,,test@demo.local,", csv);
    }
}
