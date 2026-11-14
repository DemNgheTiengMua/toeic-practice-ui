using System.Globalization;
using System.Text;

namespace Contracts;

/// <summary>
/// One completed mentor consultation row for the accounting payout CSV export (ADR-0016).
/// </summary>
public sealed record MentorPayoutExportRow(
    int BookingId,
    DateOnly SessionDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int MentorId,
    string MentorName,
    string CertCode,
    string? SkillName,
    string LearnerEmail,
    string LearnerName,
    int PriceCredits,
    decimal AmountVnd);

/// <summary>
/// Pure RFC 4180 CSV formatter for completed mentor payouts with UTF-8 BOM, ISO-8601 dates in UTC+7,
/// and bare decimal money formatting (ADR-0016, mirroring <see cref="RefundExportCsv"/>).
/// </summary>
public static class MentorPayoutExportCsv
{
    public const string ContentType = "text/csv";

    public const string Header =
        "Booking ID,Session date,Start time,End time,Mentor ID,Mentor name,Certificate,Skill,Learner email,Learner name,Credits,Money amount,Exported at";

    private const string RecordSeparator = "\r\n";

    public static byte[] Format(IReadOnlyList<MentorPayoutExportRow> rows, DateTimeOffset exportedAt)
    {
        var text = new StringBuilder();
        text.Append(Header).Append(RecordSeparator);

        var stamp = exportedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        foreach (var row in rows)
        {
            var fields = new[]
            {
                row.BookingId.ToString(CultureInfo.InvariantCulture),
                row.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                row.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                row.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                row.MentorId.ToString(CultureInfo.InvariantCulture),
                row.MentorName,
                row.CertCode,
                row.SkillName ?? "",
                row.LearnerEmail,
                row.LearnerName,
                row.PriceCredits.ToString(CultureInfo.InvariantCulture),
                row.AmountVnd.ToString("0.00", CultureInfo.InvariantCulture),
                stamp,
            };

            text.AppendJoin(',', fields.Select(Quote)).Append(RecordSeparator);
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    private static string Quote(string field) =>
        field.AsSpan().IndexOfAny(",\"\r\n") < 0
            ? field
            : $"\"{field.Replace("\"", "\"\"")}\"";
}
