using Contracts;

namespace Services;

/// <summary>
/// #247.2: the financial pass over a cutover. Two halves, because the window has two sides: take the
/// snapshot from the old database before the window, reconcile the new one after.
/// </summary>
public interface IReconciliationService
{
    /// <summary>Every wallet's balance and row count, as they stand right now.</summary>
    Task<BalanceSnapshot> SnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>Every wallet against the snapshot. A discrepancy blocks the cutover step.</summary>
    Task<ReconciliationResult> ReconcileAsync(BalanceSnapshot snapshot, CancellationToken cancellationToken = default);
}
