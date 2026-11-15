using System.Globalization;

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
/// Pure RFC 4180 CSV formatter for completed mentor payouts, over the shared writer
/// (<see cref="Rfc4180Csv"/>): UTF-8 BOM, ISO-8601 dates in UTC+7, and bare decimal money.
/// </summary>
public static class MentorPayoutExportCsv
{
    public const string ContentType = "text/csv";

    public const string Header =
        "Booking ID,Session date,Start time,End time,Mentor ID,Mentor name,Certificate,Skill,Learner email,Learner name,Credits,Money amount,Exported at";

    public static byte[] Format(IReadOnlyList<MentorPayoutExportRow> rows, DateTimeOffset exportedAt)
    {
        var stamp = Rfc4180Csv.ExportStamp(exportedAt);

        return Rfc4180Csv.Format(Header, rows.Select(row => (IReadOnlyList<string>)
        [
            row.BookingId.ToString(CultureInfo.InvariantCulture),
            Rfc4180Csv.Date(row.SessionDate),
            row.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            row.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            row.MentorId.ToString(CultureInfo.InvariantCulture),
            row.MentorName,
            row.CertCode,
            row.SkillName ?? "",
            row.LearnerEmail,
            row.LearnerName,
            Rfc4180Csv.Count(row.PriceCredits),
            Rfc4180Csv.Money(row.AmountVnd),
            stamp,
        ]).ToList());
    }
}
