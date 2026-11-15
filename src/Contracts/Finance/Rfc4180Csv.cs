using System.Globalization;
using System.Text;

namespace Contracts;

/// <summary>
/// The one RFC 4180 writer the accounting exports share (ADR-0016). Refunds, per-session payouts and
/// per-mentor totals all land in the same spreadsheet, so they make one contract: UTF-8 with a byte-order
/// mark, CRLF between records, RFC 4180 quoting, ISO-8601 dates and bare decimal money.
/// <para>
/// <b>Why one writer rather than three.</b> The promise was a private copy of the same twenty lines in each
/// formatter, so a fix to quoting reached whichever file was edited and left the other two. Nothing made
/// that visible: each file had its own test that passed against its own copy.
/// </para>
/// <para>
/// <b>UTF-8 with a byte-order mark.</b> Without the three bytes Excel on a Vietnamese Windows install
/// decodes the file as ANSI and every diacritic becomes mojibake - a learner's or a mentor's name is then
/// wrong in the one document accounting keeps.
/// </para>
/// </summary>
public static class Rfc4180Csv
{
    /// <summary>RFC 4180 uses CRLF between records, and Excel accepts nothing else consistently.</summary>
    public const string RecordSeparator = "\r\n";

    /// <summary>The bytes of the file: the BOM, the header line, then one line per row.</summary>
    public static byte[] Format(string header, IEnumerable<IReadOnlyList<string>> rows)
    {
        var text = new StringBuilder();
        text.Append(header).Append(RecordSeparator);

        foreach (var row in rows)
        {
            text.AppendJoin(',', row.Select(Quote)).Append(RecordSeparator);
        }

        // The preamble is the byte-order mark. Encoding.UTF8.GetBytes never emits it, so it is prepended
        // by hand rather than relying on an encoding flag whose behaviour differs between the writer APIs.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    /// <summary>
    /// The stamp on every row of an export: <c>yyyy-MM-dd HH:mm</c>, invariant. ISO-8601 is the shape Excel
    /// parses in every locale; <c>01/10/2026</c> is October in Hanoi and January in a US-locale install, and
    /// a payment filed in the wrong month is worse than a payment with no date.
    /// </summary>
    public static string ExportStamp(DateTimeOffset exportedAt) =>
        exportedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>An ISO-8601 date cell, empty when there is none.</summary>
    public static string Date(DateOnly? day) =>
        day?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    /// <summary>A money cell: the exact stored decimal to its two stored places, never a display string.
    /// A cell reading "150,000 ₫" arrives in a spreadsheet as text or as two cells, and accounting cannot
    /// sum a column of prose. <see cref="CurrencyFormatter"/> remains the only way money is stated on a
    /// screen. Empty when there is no amount, so a zero cannot be mistaken for one.</summary>
    public static string Money(decimal? amount) =>
        amount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "";

    /// <summary>A count cell, invariant, empty when there is none.</summary>
    public static string Count(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "";

    /// <summary>
    /// RFC 4180: a field is quoted when it contains a comma, a quote, CR or LF, and an embedded quote is
    /// doubled. A reason is free text up to 400 characters and a learner or mentor name can contain a comma,
    /// so all four cases are reachable from real data - an unquoted one would shift every column to its right.
    /// </summary>
    public static string Quote(string field) =>
        field.AsSpan().IndexOfAny(",\"\r\n") < 0
            ? field
            : $"\"{field.Replace("\"", "\"\"")}\"";
}
