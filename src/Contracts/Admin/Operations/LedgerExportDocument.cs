using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>
/// #246.1: one ledger row, exported for a cutover. Every column the table holds is here, because the
/// ticket's criterion is "re-imported with every historical reason string intact" and a row that lost
/// <see cref="Reason"/> would come back under a different one - which is a rewrite of history, not a
/// transfer of it.
/// <para>
/// <see cref="TxnId"/> travels. The ledger is append-only and enforced by <c>trg_CreditTransactions_NoRewrite</c>,
/// which compares the row by id; keeping the id is what lets an import be idempotent - the second run
/// finds the id already present and writes nothing - instead of doubling a wallet.
/// </para>
/// </summary>
public sealed record LedgerExportRow(
    int TxnId,
    int UserId,
    string Reason,
    int Delta,
    int? OrderId,
    int? AttemptId,
    int? BookingId,
    int? AdminUserId,
    string? Note,
    DateTimeOffset CreatedAt);

/// <summary>
/// #246.1: the whole ledger, as one document. The counts are carried so an import can prove it received
/// everything before it writes anything: a truncated file that silently imports half a ledger is the
/// failure this shape exists to refuse.
/// </summary>
public sealed record LedgerExportDocument(
    int SchemaVersion,
    DateTimeOffset ExportedAt,
    int RowCount,
    IReadOnlyList<LedgerExportRow> Rows)
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static LedgerExportDocument? FromJson(string json) =>
        JsonSerializer.Deserialize<LedgerExportDocument>(json, Json);
}

/// <summary>
/// #246.6: what an import did, so a cutover can tell "already imported" from "imported nothing because
/// the file was empty". The three counts are disjoint and add up to <see cref="RowsInFile"/>.
/// </summary>
public sealed record LedgerImportResult(
    int RowsInFile,
    int Inserted,
    int AlreadyPresent,
    int Refused)
{
    /// <summary>
    /// The import is a success when it wrote every row that was missing and refused none. A refusal is
    /// not a warning: a row that cannot be written means the wallet would come back smaller than it
    /// left, which is the one thing a cutover must not do silently.
    /// </summary>
    public bool Reconciled => Refused == 0 && Inserted + AlreadyPresent == RowsInFile;
}
