using System.Globalization;
using System.Text;

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
/// decimal money - because accounting opens both in the same spreadsheet.
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

    /// <summary>RFC 4180 uses CRLF between records, and Excel accepts nothing else consistently.</summary>
    private const string RecordSeparator = "\r\n";

    public static byte[] Format(IReadOnlyList<MentorPayoutTotalsRow> rows, DateTimeOffset exportedAt)
    {
        var text = new StringBuilder();
        text.Append(Header).Append(RecordSeparator);

        var stamp = exportedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        foreach (var row in rows)
        {
            var fields = new[]
            {
                row.MentorId.ToString(CultureInfo.InvariantCulture),
                row.MentorName,
                row.CertCode,
                row.SessionCount.ToString(CultureInfo.InvariantCulture),
                row.TotalCredits.ToString(CultureInfo.InvariantCulture),
                row.TotalAmountVnd.ToString("0.00", CultureInfo.InvariantCulture),
                stamp,
            };

            text.AppendJoin(',', fields.Select(Quote)).Append(RecordSeparator);
        }

        // A period with nothing delivered yields this header and nothing else: an empty file accounting
        // cannot open is worse than a file that states there is nothing to pay.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    private static string Quote(string field) =>
        field.AsSpan().IndexOfAny(",\"\r\n") < 0
            ? field
            : $"\"{field.Replace("\"", "\"\"")}\"";
}
