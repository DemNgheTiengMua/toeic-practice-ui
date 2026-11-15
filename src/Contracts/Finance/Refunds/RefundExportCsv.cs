using System.Globalization;

namespace Contracts;

/// <summary>
/// #283: one refund, as accounting reads it. The dates are <see cref="DateOnly"/> because the reporting
/// day is decided before this row exists: a <see cref="DateTimeOffset"/> here would let an offset leak
/// into the file and print a payment on the wrong day.
/// <para>
/// <see cref="Amount"/> and <see cref="CreditCount"/> are both nullable on purpose. The method owns which
/// one is set (<c>CK_Refunds_MethodOwnsAmount</c>), so a row carries exactly one and the other stays
/// empty rather than becoming a zero accounting cannot tell from a real one.
/// </para>
/// </summary>
public sealed record RefundExportRow(
    int RefundId,
    DateOnly RequestedOn,
    DateOnly? SettledOn,
    string LearnerEmail,
    string LearnerName,
    string? OrderCode,
    RefundMethodDto Method,
    decimal? Amount,
    int? CreditCount,
    string Reason,
    string? DecidingAdmin);

/// <summary>
/// #283: the accounting export, as a pure function over rows. No database, no request, no clock: the
/// caller passes the rows and the moment the export ran, so the file's bytes can be asserted in a test
/// without an HTTP host.
/// <para>
/// <b>UTF-8 with a byte-order mark.</b> Without the three bytes Excel on a Vietnamese Windows install
/// decodes the file as ANSI and every diacritic becomes mojibake - the learner's name is then wrong in the
/// one document accounting keeps.
/// </para>
/// <para>
/// <b>Dates are ISO-8601 (<c>yyyy-MM-dd</c>), invariant culture.</b> Excel parses that shape in every
/// locale; <c>01/10/2026</c> is October in Hanoi and January in a US-locale install, and a payment filed
/// in the wrong month is worse than a payment with no date. The export stamp adds the reporting time in
/// the same shape, because a bare time-of-day invites the same misreading.
/// </para>
/// <para>
/// <b>Money is a bare number, not a display string.</b> <see cref="CurrencyFormatter"/> states an amount
/// for a human to read ("150,000 ₫"); a cell that says that arrives in a spreadsheet as text or as two
/// cells, and accounting cannot sum a column of prose. So the amount is the exact stored decimal to its
/// two stored places, and the currency is the header's business. <see cref="CurrencyFormatter"/> remains
/// the only way money is stated on a screen.
/// </para>
/// </summary>
public static class RefundExportCsv
{
    public const string ContentType = "text/csv";

    public const string Header =
        "Refund ID,Requested on,Settled on,Learner email,Learner name,Order code,Method,Money amount,Credit count,Reason,Decided by,Exported at";

    /// <summary>
    /// The bytes of the file. <paramref name="exportedAt"/> is stamped on every row and changes no
    /// refund's status: an export is a read, so exporting twice yields the same rows.
    /// </summary>
    public static byte[] Format(IReadOnlyList<RefundExportRow> rows, DateTimeOffset exportedAt)
    {
        var stamp = Rfc4180Csv.ExportStamp(exportedAt);

        return Rfc4180Csv.Format(Header, rows.Select(row => (IReadOnlyList<string>)
        [
            row.RefundId.ToString(CultureInfo.InvariantCulture),
            Rfc4180Csv.Date(row.RequestedOn),
            Rfc4180Csv.Date(row.SettledOn),
            row.LearnerEmail,
            row.LearnerName,
            row.OrderCode ?? "",
            row.Method.ToWireCode(),
            Rfc4180Csv.Money(row.Amount),
            Rfc4180Csv.Count(row.CreditCount),
            row.Reason,
            row.DecidingAdmin ?? "",
            stamp,
        ]).ToList());
    }
}
