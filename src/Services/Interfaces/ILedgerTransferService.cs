using Contracts;

namespace Services;

/// <summary>
/// #246.1 / #246.6: the ledger's export and re-import across a cutover. Separate from
/// <see cref="ICreditLedger"/> because that type writes NEW transactions and enforces block-before-charge;
/// this one transfers EXISTING rows and must never invent one.
/// </summary>
public interface ILedgerTransferService
{
    /// <summary>The whole ledger, every column, ordered by id so two exports of the same data agree.</summary>
    Task<LedgerExportDocument> ExportAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the rows the target database does not already hold, and reports what it did. Running it
    /// twice must not duplicate a row: the id travels, and a present id is a no-op.
    /// </summary>
    Task<LedgerImportResult> ImportAsync(LedgerExportDocument document, CancellationToken cancellationToken = default);
}
