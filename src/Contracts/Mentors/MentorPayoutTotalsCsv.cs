using System.Globalization;

namespace Contracts;

/// <summary>
/// #353: one mentor's total for a period, so accounting makes one transfer per row instead of summing the
/// session export by hand.
/// <para>
/// Deliberately carries no bank details and no "already paid" marker: both are kept outside the product, so
/// a file that implied either would state something nobody recorded.
/// </para>
/// </summary>
public sealed record MentorPayoutTotalsRow(
    int MentorId,
    string MentorName,
    string CertCode,
    int SessionCount,
    int TotalCredits,
    decimal TotalAmountVnd);

/// <summary>
/// #353: the second accounting file. One row per mentor, same contract as
/// <see cref="MentorPayoutExportCsv"/> - UTF-8 byte-order mark, RFC 4180 quoting, ISO-8601 dates, bare
/// decimal money - because accounting opens both in the same spreadsheet. Both are written by
/// <see cref="Rfc4180Csv"/> so that contract cannot drift between them.
/// <para>
/// Two files rather than one: mixing per-session and per-mentor rows inside a single CSV breaks Excel's
/// column typing and every importer that reads the header.
/// </para>
/// </summary>
public static class MentorPayoutTotalsCsv
{
    public const string ContentType = "text/csv";

    public const string Header =
        "Mentor ID,Mentor name,Certificate,Sessions,Credits,Money amount,Exported at";

    public static byte[] Format(IReadOnlyList<MentorPayoutTotalsRow> rows, DateTimeOffset exportedAt)
    {
        var stamp = Rfc4180Csv.ExportStamp(exportedAt);

        // A period with nothing delivered yields this header and nothing else: an empty file accounting
        // cannot open is worse than a file that states there is nothing to pay.
        return Rfc4180Csv.Format(Header, rows.Select(row => (IReadOnlyList<string>)
        [
            row.MentorId.ToString(CultureInfo.InvariantCulture),
            row.MentorName,
            row.CertCode,
            Rfc4180Csv.Count(row.SessionCount),
            Rfc4180Csv.Count(row.TotalCredits),
            Rfc4180Csv.Money(row.TotalAmountVnd),
            stamp,
        ]).ToList());
    }
}
