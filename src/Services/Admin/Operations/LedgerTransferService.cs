using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

/// <summary>
/// #246.1 and #246.6: the ledger crosses a cutover by export and re-import, and the import is
/// idempotent.
/// <para>
/// <b>Why <c>TxnId</c> travels.</b> The ledger is append-only: <c>trg_CreditTransactions_NoRewrite</c>
/// refuses any change to a posted row, and <c>trg_CreditTransactions_AppendOnly</c> refuses its
/// deletion. An import that minted new identities would therefore be unable to tell "this row is
/// already here" from "this row is new", and running it twice would double every wallet. Carrying the
/// id makes the second run a no-op, which is what the criterion asks for.
/// </para>
/// <para>
/// <b>Why the export carries the row count.</b> A file truncated in transit would import as a smaller
/// ledger that looks complete. The count is compared before a single row is written, so the failure is
/// "the file is short", not "the wallets are smaller than they were".
/// </para>
/// <para>
/// <b>Why every reason string is preserved verbatim.</b> The vocabulary is history: <c>admin_grant</c>
/// from two years ago means what it meant then, and a re-import that renamed or dropped it would be
/// rewriting the record. This service never maps a reason; it writes the exact string it read, and the
/// CHECK constraint is what decides whether the vocabulary still admits it.
/// </para>
/// </summary>
public sealed class LedgerTransferService(CompanionDbContext db, TimeProvider clock) : ILedgerTransferService
{
    public async Task<LedgerExportDocument> ExportAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.CreditTransactions
            .AsNoTracking()
            .OrderBy(t => t.TxnId)
            .Select(t => new LedgerExportRow(
                t.TxnId, t.UserId, t.Reason, t.Delta,
                t.OrderId, t.AttemptId, t.BookingId, t.AdminUserId, t.Note, t.CreatedAt))
            .ToListAsync(cancellationToken);

        return new LedgerExportDocument(
            LedgerExportDocument.CurrentSchemaVersion,
            clock.GetUtcNow(),
            rows.Count,
            rows);
    }

    public async Task<LedgerImportResult> ImportAsync(
        LedgerExportDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.SchemaVersion != LedgerExportDocument.CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"The ledger export is schema version {document.SchemaVersion} and this build reads "
                + $"{LedgerExportDocument.CurrentSchemaVersion}. Refusing to guess at a format it does not know.");
        }

        // The count check runs BEFORE any write: a short file must not produce a short ledger.
        if (document.RowCount != document.Rows.Count)
        {
            throw new InvalidOperationException(
                $"The ledger export declares {document.RowCount} row(s) and carries {document.Rows.Count}. "
                + "A truncated file would import as a smaller ledger that looks complete, so nothing was written.");
        }

        if (document.Rows.Count == 0)
        {
            return new LedgerImportResult(0, 0, 0, 0);
        }

        var ids = document.Rows.Select(r => r.TxnId).ToList();
        var existing = await db.CreditTransactions
            .AsNoTracking()
            .Where(t => ids.Contains(t.TxnId))
            .Select(t => t.TxnId)
            .ToListAsync(cancellationToken);

        var present = existing.ToHashSet();
        var rowsToInsert = document.Rows.Where(r => !present.Contains(r.TxnId)).ToList();
        var already = document.Rows.Count - rowsToInsert.Count;

        if (rowsToInsert.Count == 0)
        {
            return new LedgerImportResult(document.Rows.Count, 0, already, 0);
        }

        foreach (var row in rowsToInsert)
        {
            db.CreditTransactions.Add(new BusinessObjects.Entities.CreditTransaction
            {
                // Identity insert: the id is part of the fact being preserved, not an artefact.
                TxnId = row.TxnId,
                UserId = row.UserId,
                Reason = row.Reason,
                Delta = row.Delta,
                OrderId = row.OrderId,
                AttemptId = row.AttemptId,
                BookingId = row.BookingId,
                AdminUserId = row.AdminUserId,
                Note = row.Note,
                CreatedAt = row.CreatedAt,
            });
        }

        try
        {
            // TxnId is an IDENTITY column, so a preserved id only lands with IDENTITY_INSERT on. It is
            // session-scoped and applies to ONE table at a time, which is why it is turned on for this
            // table only and turned off in the same transaction. Without it every import row is refused
            // and the wallets come back empty - measured: inserted=0 already=0 refused=1.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                "SET IDENTITY_INSERT CreditTransactions ON", cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                "SET IDENTITY_INSERT CreditTransactions OFF", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A row the vocabulary or the source-shape CHECK no longer admits. The count is reported
            // rather than swallowed: a cutover that silently skipped a row would leave a wallet short.
            return new LedgerImportResult(document.Rows.Count, 0, already, rowsToInsert.Count);
        }

        return new LedgerImportResult(document.Rows.Count, rowsToInsert.Count, already, 0);
    }
}
